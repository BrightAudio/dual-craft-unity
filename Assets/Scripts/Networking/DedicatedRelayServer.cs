using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace DualCraft.Networking
{
    using Cards;
    using Core;

    /// <summary>
    /// Headless two-client authority. Relay transports packets, but this process owns
    /// deck validation, hidden information, action validation, and the match state.
    /// </summary>
    public sealed class DedicatedRelayServer : MonoBehaviour
    {
        private const string ChunkMagic = "DUALMON_CHUNK_V1";
        private sealed class ClientSlot
        {
            public NetworkConnection Connection;
            public int Seat = -1;
            public string PlayerId;
            public readonly Dictionary<string, ChunkAccumulator> Chunks = new();
        }

        [Serializable]
        private sealed class RelayChunk
        {
            public string Magic;
            public string MessageId;
            public int Index;
            public int Total;
            public string Payload;
        }

        private sealed class ChunkAccumulator
        {
            public readonly string[] Chunks;
            public int Received;

            public ChunkAccumulator(int total)
            {
                Chunks = new string[Mathf.Max(1, total)];
            }
        }

        [Serializable]
        private sealed class ServerReadyInfo
        {
            public string JoinCode;
            public string BuildId;
            public int ProtocolVersion;
            public string StartedUtc;
        }

        private NetworkDriver _driver;
        private NetworkPipeline _reliablePipeline;
        private readonly List<ClientSlot> _clients = new(2);
        private CardDatabase _cardDb;
        private AuthoritativeRoom _room;
        private readonly DeckData[] _decks = new DeckData[2];
        private string _joinCode;
        private bool _ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootForServerBuild()
        {
#if UNITY_SERVER
            CreateServerObject();
#else
            if (Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "-dualmon-server", StringComparison.OrdinalIgnoreCase)))
                CreateServerObject();
#endif
        }

        private static void CreateServerObject()
        {
            if (FindAnyObjectByType<DedicatedRelayServer>() != null)
                return;

            var server = new GameObject("DualMon Dedicated Server");
            DontDestroyOnLoad(server);
            server.AddComponent<DedicatedRelayServer>();
        }

        private async void Start()
        {
            Application.runInBackground = true;
            _cardDb = RuntimeAssetLocator.LoadCardDatabase(null, this);
            if (_cardDb == null)
            {
                Fail("Card database is missing from the server build.");
                return;
            }
            _cardDb.Initialize();

            try
            {
                await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(2);
                _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                var relayData = new RelayServerData(allocation, "dtls");
                var settings = new NetworkSettings();
                settings.WithRelayParameters(ref relayData);
                settings.WithReliableStageParameters(windowSize: 64, minimumResendTime: 64, maximumResendTime: 500);
                _driver = NetworkDriver.Create(settings);
                _reliablePipeline = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                {
                    Fail("Could not bind the dedicated Relay driver.");
                    return;
                }

                _driver.Listen();
                _room = new AuthoritativeRoom(_joinCode, new RoomSettings
                {
                    TurnTimerSeconds = 90,
                    GameMode = "standard",
                });
                _room.OnSendToPlayer += SendToSeat;
                _room.OnRoomClosed += _ => Debug.Log("[DedicatedServer] Match finished; server remains available for result delivery.");
                _ready = true;
                WriteReadyFile();
                Debug.Log($"[DedicatedServer] READY DUALMON_JOIN_CODE={_joinCode} BUILD={MultiplayerProtocol.BuildId}");
            }
            catch (Exception ex)
            {
                Fail($"Startup failed: {ex}");
            }
        }

        private void Update()
        {
            if (!_ready || !_driver.IsCreated)
                return;

            _driver.ScheduleUpdate().Complete();
            NetworkConnection incoming;
            while ((incoming = _driver.Accept()) != default)
            {
                if (_clients.Count >= 2)
                {
                    _driver.Disconnect(incoming);
                    continue;
                }

                _clients.Add(new ClientSlot { Connection = incoming });
                Debug.Log($"[DedicatedServer] Transport client connected ({_clients.Count}/2).");
            }

            for (int i = _clients.Count - 1; i >= 0; i--)
                PumpClient(_clients[i], i);
        }

        private void PumpClient(ClientSlot client, int listIndex)
        {
            DataStreamReader reader;
            NetworkEvent.Type evt;
            while ((evt = _driver.PopEventForConnection(client.Connection, out reader)) != NetworkEvent.Type.Empty)
            {
                if (evt == NetworkEvent.Type.Data)
                {
                    var bytes = new byte[reader.Length];
                    reader.ReadBytes(bytes);
                    if (TryReassemble(client, bytes, out byte[] message))
                        HandleMessage(client, message);
                }
                else if (evt == NetworkEvent.Type.Disconnect)
                {
                    if (client.Seat >= 0)
                        _room?.PlayerDisconnected(client.Seat);
                    Debug.LogWarning($"[DedicatedServer] Seat {client.Seat} disconnected.");
                    _clients.RemoveAt(listIndex);
                    return;
                }
            }
        }

        private void HandleMessage(ClientSlot client, byte[] bytes)
        {
            NetEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<NetEnvelope>(Encoding.UTF8.GetString(bytes));
            }
            catch
            {
                SendError(client, "BAD_MESSAGE", "The server could not read that network message.");
                return;
            }

            switch (envelope.Type)
            {
                case nameof(JoinRoomRequest):
                    HandleJoin(client, JsonUtility.FromJson<JoinRoomRequest>(envelope.Payload));
                    break;
                case nameof(ActionRequest):
                    HandleAction(client, JsonUtility.FromJson<ActionRequest>(envelope.Payload));
                    break;
                case nameof(PrivateStateRequest):
                    if (client.Seat >= 0)
                        _room?.SendStateSnapshot(client.Seat);
                    break;
                case nameof(ForfeitRequest):
                    if (client.Seat >= 0)
                        _room?.ForfeitPlayer(client.Seat);
                    break;
                case nameof(LeaveRequest):
                    if (client.Seat >= 0)
                        _room?.PlayerDisconnected(client.Seat);
                    break;
                case nameof(PingMessage):
                    var ping = JsonUtility.FromJson<PingMessage>(envelope.Payload);
                    Send(client, new PongMessage
                    {
                        ClientTime = ping?.ClientTime ?? 0,
                        ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    });
                    break;
                case nameof(StateAppliedAck):
                    var ack = JsonUtility.FromJson<StateAppliedAck>(envelope.Payload);
                    if (ack != null)
                        Debug.Log($"[DedicatedServer] Seat {client.Seat} rendered state #{ack.ServerSequence}: {ack.RenderedNamedCardCount}/{ack.RenderedHandCount} named.");
                    break;
            }
        }

        private void HandleJoin(ClientSlot client, JoinRoomRequest request)
        {
            if (request == null)
            {
                SendError(client, "BAD_JOIN", "Join data was empty.");
                return;
            }
            if (request.ProtocolVersion != MultiplayerProtocol.CurrentVersion
                || !string.Equals(request.BuildId, MultiplayerProtocol.BuildId, StringComparison.Ordinal))
            {
                SendError(client, "BUILD_MISMATCH",
                    $"Server requires {MultiplayerProtocol.BuildId}; client reported {request.BuildId ?? "unknown"}.");
                return;
            }
            if (client.Seat >= 0)
            {
                _room?.PlayerReconnected(client.Seat);
                return;
            }
            int reconnectSeat = Array.FindIndex(_room?.Players ?? Array.Empty<PlayerSession>(),
                player => player != null && string.Equals(player.PlayerId, request.PlayerId, StringComparison.Ordinal));
            if (reconnectSeat >= 0 && _clients.All(item => item.Seat != reconnectSeat))
            {
                client.Seat = reconnectSeat;
                client.PlayerId = request.PlayerId;
                SendRoomStatus();
                _room.PlayerReconnected(reconnectSeat);
                Debug.Log($"[DedicatedServer] Player {request.PlayerName} reconnected to seat {reconnectSeat}.");
                return;
            }
            if (_room == null || _room.Started)
            {
                SendError(client, "ROOM_FULL", "This dedicated room is already in a match.");
                return;
            }

            DeckData deck = ReconstructDeck(request);
            if (deck == null || !deck.IsValid)
            {
                SendError(client, "INVALID_DECK", $"The submitted deck must contain {GameConstants.DeckSize} valid cards.");
                return;
            }

            int seat = _room.AddPlayer(new PlayerSession
            {
                PlayerId = request.PlayerId,
                PlayerName = string.IsNullOrWhiteSpace(request.PlayerName) ? $"Player {_room.ConnectedCount + 1}" : request.PlayerName,
                DeckId = request.DeckId,
                AuthToken = request.AuthToken,
            });
            if (seat < 0)
            {
                SendError(client, "ROOM_FULL", "This dedicated room already has two players.");
                return;
            }

            client.Seat = seat;
            client.PlayerId = request.PlayerId;
            _decks[seat] = deck;
            SendRoomStatus();
            Debug.Log($"[DedicatedServer] {request.PlayerName} joined seat {seat} with {deck.deckName} ({deck.primaryCreatureType}).");

            if (_room.ConnectedCount == 2)
            {
                _room.StartGame(_cardDb, _decks[0], _decks[1]);
                Debug.Log("[DedicatedServer] Authoritative match started.");
            }
            else
            {
                Send(client, new WaitingForOpponent
                {
                    RoomId = _joinCode,
                    Message = "Connected to cloud server. Waiting for opponent.",
                });
            }
        }

        private void SendRoomStatus()
        {
            foreach (ClientSlot client in _clients.Where(item => item.Seat >= 0))
            {
                int opponentSeat = 1 - client.Seat;
                Send(client, new RoomJoined
                {
                    RoomId = _joinCode,
                    PlayerIndex = client.Seat,
                    OpponentName = _room.Players[opponentSeat]?.PlayerName ?? "Waiting...",
                    Settings = _room.Settings,
                    GameStarted = _room.ConnectedCount == 2,
                });
            }
        }

        private void HandleAction(ClientSlot client, ActionRequest request)
        {
            if (client.Seat < 0 || request?.Action == null || _room?.Started != true)
                return;

            // The connection owns the seat. Never trust PlayerIndex from the client.
            _room.ProcessAction(client.Seat, request.Action, request.SequenceNum);
        }

        private DeckData ReconstructDeck(JoinRoomRequest request)
        {
            if (request.MainDeck == null || request.MainDeck.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(request.DeckId)
                    && request.DeckId.StartsWith("prebuilt:", StringComparison.OrdinalIgnoreCase))
                {
                    string name = request.DeckId.Substring("prebuilt:".Length);
                    return Resources.LoadAll<DeckData>("CardData/Decks")
                        .FirstOrDefault(deck => deck != null && deck.deckName == name)
                        ?.CreatePlayableRuntimeCopy();
                }
                return null;
            }

            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = $"{request.PlayerName}'s Deck";
            deck.element = ParseEnum(request.DeckElement, Element.Flame);
            deck.primaryCreatureType = ParseEnum(request.DeckArchetype, CreatureType.Elemental);
            deck.invokerCard = !string.IsNullOrWhiteSpace(request.InvokerCardId)
                ? _cardDb.GetCard(request.InvokerCardId) as InvokerCardData
                : null;
            deck.cards = ResolveEntries(request.MainDeck).ToArray();
            deck.pillars = ResolveEntries(request.PillarDeck).ToArray();
            deck.wardIds = request.WardIds?.ToArray() ?? Array.Empty<string>();
            return deck.CreatePlayableRuntimeCopy();
        }

        private IEnumerable<DeckEntry> ResolveEntries(IEnumerable<SerializableDeckEntry> entries)
        {
            if (entries == null)
                yield break;

            foreach (SerializableDeckEntry entry in entries)
            {
                CardData card = _cardDb.GetCard(entry.CardId);
                if (card != null && entry.Count > 0)
                    yield return new DeckEntry { card = card, count = entry.Count };
            }
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
        }

        private void SendToSeat(int seat, NetEnvelope envelope)
        {
            ClientSlot client = _clients.FirstOrDefault(item => item.Seat == seat);
            if (client != null)
                SendEnvelope(client, envelope);
        }

        private void Send<T>(ClientSlot client, T message) where T : class
        {
            SendEnvelope(client, NetEnvelope.Create(message, "dedicated-server"));
        }

        private void SendError(ClientSlot client, string code, string message)
        {
            Debug.LogError($"[DedicatedServer] {code}: {message}");
            Send(client, new ServerError { Code = code, Message = message });
        }

        private void SendEnvelope(ClientSlot client, NetEnvelope envelope)
        {
            if (client == null || envelope == null || !_driver.IsCreated || !client.Connection.IsCreated)
                return;

            byte[] full = Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope));
            IReadOnlyList<byte[]> packets = RelayManager.BuildTransportPackets(full);
            foreach (byte[] packet in packets)
            {
                int begin = _driver.BeginSend(_reliablePipeline, client.Connection, out DataStreamWriter writer, packet.Length);
                if (begin != 0)
                {
                    Debug.LogError($"[DedicatedServer] BeginSend failed: {begin}.");
                    return;
                }
                writer.WriteBytes(packet);
                int end = _driver.EndSend(writer);
                if (end < 0)
                    Debug.LogError($"[DedicatedServer] EndSend failed: {end}.");
            }
        }

        private static bool TryReassemble(ClientSlot client, byte[] packet, out byte[] message)
        {
            message = packet;
            string json;
            try { json = Encoding.UTF8.GetString(packet); }
            catch { return true; }
            if (string.IsNullOrWhiteSpace(json) || !json.Contains(ChunkMagic))
                return true;

            RelayChunk chunk;
            try { chunk = JsonUtility.FromJson<RelayChunk>(json); }
            catch { return true; }
            if (chunk == null || chunk.Magic != ChunkMagic || chunk.Total <= 0
                || chunk.Index < 0 || chunk.Index >= chunk.Total || string.IsNullOrWhiteSpace(chunk.Payload))
                return true;

            if (!client.Chunks.TryGetValue(chunk.MessageId, out ChunkAccumulator accumulator)
                || accumulator.Chunks.Length != chunk.Total)
            {
                accumulator = new ChunkAccumulator(chunk.Total);
                client.Chunks[chunk.MessageId] = accumulator;
            }
            if (accumulator.Chunks[chunk.Index] == null)
            {
                accumulator.Chunks[chunk.Index] = chunk.Payload;
                accumulator.Received++;
            }
            if (accumulator.Received < accumulator.Chunks.Length)
            {
                message = null;
                return false;
            }

            var decoded = accumulator.Chunks.Select(Convert.FromBase64String).ToArray();
            message = new byte[decoded.Sum(bytes => bytes.Length)];
            int offset = 0;
            foreach (byte[] bytes in decoded)
            {
                Buffer.BlockCopy(bytes, 0, message, offset, bytes.Length);
                offset += bytes.Length;
            }
            client.Chunks.Remove(chunk.MessageId);
            return true;
        }

        private void WriteReadyFile()
        {
            var info = new ServerReadyInfo
            {
                JoinCode = _joinCode,
                BuildId = MultiplayerProtocol.BuildId,
                ProtocolVersion = MultiplayerProtocol.CurrentVersion,
                StartedUtc = DateTime.UtcNow.ToString("O"),
            };
            string path = Path.Combine(Application.persistentDataPath, "dualmon-server-ready.json");
            File.WriteAllText(path, JsonUtility.ToJson(info));
            Debug.Log($"[DedicatedServer] Ready file: {path}");
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[DedicatedServer] FATAL {message}");
            Application.Quit(1);
        }

        private void OnDestroy()
        {
            if (_room != null)
                _room.OnSendToPlayer -= SendToSeat;
            if (_driver.IsCreated)
                _driver.Dispose();
        }
    }
}
