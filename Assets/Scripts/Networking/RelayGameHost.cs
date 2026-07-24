// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Relay Game Host
//
//  Bridges RelayManager (transport) ↔ AuthoritativeRoom (game logic).
//  The HOST player runs this: it receives actions from
//  the remote client via Relay, feeds them to
//  AuthoritativeRoom, and sends state updates back.
//
//  From the remote client's perspective, it looks exactly
//  like talking to a dedicated server — they never know
//  the "server" is the other player's game client.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DualCraft.Networking
{
    using Battle;
    using Cards;
    using Core;
    using Data;

    /// <summary>
    /// Runs on the HOST player's machine. Manages the
    /// AuthoritativeRoom and routes messages through Relay.
    /// </summary>
    public class RelayGameHost : MonoBehaviour
    {
        private RelayManager _relay;
        private AuthoritativeRoom _room;
        private CardDatabase _cardDb;
        private DeckData _hostDeck;
        private DeckData _guestDeck;
        private string _hostPlayerId;
        private bool _gameStarted;
        private Coroutine _startSnapshotBurst;
        private Coroutine _stateHeartbeat;
        private Coroutine _gameOverBurst;
        private Coroutine _guestSyncWatchdog;
        private NetEnvelope _lastGuestStateEnvelope;
        private int _lastGuestStateSequence = -1;
        private int _lastGuestReceivedSequence = -1;
        private int _lastGuestAppliedSequence = -1;
        private float _lastGuestResendAt = -999f;

        /// <summary>The room being hosted.</summary>
        public AuthoritativeRoom Room => _room;

        // Events for the local host UI
        public event Action OnGuestJoined;
        public event Action OnGameStarted;
        public event Action<int, string> OnGameEnded;
        public event Action<SyncStatus> OnGuestSyncStatusChanged;

        public int LastGuestStateSequence => _lastGuestStateSequence;
        public int LastGuestAppliedSequence => _lastGuestAppliedSequence;
        public bool GuestSynced => _lastGuestStateSequence >= 0
            && _lastGuestAppliedSequence >= _lastGuestStateSequence;

        /// <summary>
        /// Initialize the host with game data.
        /// Call this after RelayManager.StartHost() succeeds.
        /// </summary>
        public void Initialize(CardDatabase cardDb, DeckData hostDeck,
                               string hostPlayerName, string hostPlayerId)
        {
            ResetForNewRoom();
            _cardDb = cardDb;
            _hostDeck = hostDeck;
            _hostPlayerId = hostPlayerId;
            _relay = RelayManager.Instance;

            // Create the authoritative room
            _room = new AuthoritativeRoom(
                _relay.JoinCode,
                new RoomSettings { TurnTimerSeconds = 90, GameMode = "standard" }
            );

            // Host is always seat 0
            _room.AddPlayer(new PlayerSession
            {
                PlayerId = hostPlayerId,
                PlayerName = hostPlayerName,
                DeckId = "host_deck",
            });

            // When the room wants to send a message to a player,
            // we route it: seat 0 = local host, seat 1 = remote via Relay
            _room.OnSendToPlayer += HandleOutgoingMessage;

            // Listen for the remote client connecting
            _relay.OnClientConnected += HandleGuestConnected;
            _relay.OnDataReceived += HandleIncomingData;
            _relay.OnClientDisconnected += HandleGuestDisconnected;
        }

        private void ResetForNewRoom()
        {
            if (_room != null)
                _room.OnSendToPlayer -= HandleOutgoingMessage;

            if (_relay != null)
            {
                _relay.OnClientConnected -= HandleGuestConnected;
                _relay.OnDataReceived -= HandleIncomingData;
                _relay.OnClientDisconnected -= HandleGuestDisconnected;
            }

            if (_startSnapshotBurst != null)
                StopCoroutine(_startSnapshotBurst);
            if (_stateHeartbeat != null)
                StopCoroutine(_stateHeartbeat);
            if (_gameOverBurst != null)
                StopCoroutine(_gameOverBurst);
            if (_guestSyncWatchdog != null)
                StopCoroutine(_guestSyncWatchdog);

            _startSnapshotBurst = null;
            _stateHeartbeat = null;
            _gameOverBurst = null;
            _guestSyncWatchdog = null;
            _room = null;
            _guestDeck = null;
            _gameStarted = false;
            _lastGuestStateEnvelope = null;
            _lastGuestStateSequence = -1;
            _lastGuestReceivedSequence = -1;
            _lastGuestAppliedSequence = -1;
            _lastGuestResendAt = -999f;

            // MultiplayerMenu subscribes again immediately after Initialize.
            OnGuestJoined = null;
            OnGameStarted = null;
            OnGameEnded = null;
            OnGuestSyncStatusChanged = null;
        }

        public void Deactivate()
        {
            ResetForNewRoom();
        }

        private void OnDestroy()
        {
            if (_relay != null)
            {
                _relay.OnClientConnected -= HandleGuestConnected;
                _relay.OnDataReceived -= HandleIncomingData;
                _relay.OnClientDisconnected -= HandleGuestDisconnected;
            }

            if (_guestSyncWatchdog != null)
                StopCoroutine(_guestSyncWatchdog);
        }

        // ═════════════════════════════════════════════════
        //  GUEST LIFECYCLE
        // ═════════════════════════════════════════════════

        private void HandleGuestConnected()
        {
            Debug.Log("[RelayGameHost] Guest connected, waiting for their deck info...");
            OnGuestJoined?.Invoke();
        }

        private void HandleGuestDisconnected()
        {
            if (_gameStarted)
                _room.PlayerDisconnected(1);
        }

        // ═════════════════════════════════════════════════
        //  MESSAGE ROUTING
        // ═════════════════════════════════════════════════

        /// <summary>
        /// Handle messages FROM the remote client (received via Relay).
        /// </summary>
        private void HandleIncomingData(byte[] data)
        {
            string json = Encoding.UTF8.GetString(data);
            NetEnvelope envelope;
            try { envelope = JsonUtility.FromJson<NetEnvelope>(json); }
            catch { Debug.LogWarning("[RelayGameHost] Bad envelope from client."); return; }

            switch (envelope.Type)
            {
                case nameof(JoinRoomRequest):
                    HandleJoinRequest(envelope);
                    break;

                case nameof(ActionRequest):
                    HandleActionRequest(envelope);
                    break;

                case nameof(LeaveRequest):
                    HandleLeaveRequest();
                    break;

                case nameof(ForfeitRequest):
                    HandleForfeitRequest();
                    break;

                case nameof(PingMessage):
                    HandlePing(envelope);
                    break;

                case nameof(StateAppliedAck):
                    HandleStateAppliedAck(envelope);
                    break;

                case nameof(PrivateStateRequest):
                    HandlePrivateStateRequest(envelope);
                    break;

                default:
                    Debug.LogWarning($"[RelayGameHost] Unknown message: {envelope.Type}");
                    break;
            }
        }

        private void HandleJoinRequest(NetEnvelope envelope)
        {
            var req = JsonUtility.FromJson<JoinRoomRequest>(envelope.Payload);
            if (req == null)
            {
                Debug.LogWarning("[RelayGameHost] Empty join request.");
                return;
            }

            if (req.ProtocolVersion != MultiplayerProtocol.CurrentVersion
                || !string.Equals(req.BuildId, MultiplayerProtocol.BuildId, StringComparison.Ordinal))
            {
                string message = $"Multiplayer build mismatch. Host requires {MultiplayerProtocol.BuildId} (protocol {MultiplayerProtocol.CurrentVersion}); guest reported {req.BuildId ?? "unknown"} (protocol {req.ProtocolVersion}).";
                Debug.LogError($"[RelayGameHost] {message}");
                PublishGuestSyncStatus(message);
                SendRemoteEnvelope(NetEnvelope.Create(new ServerError
                {
                    Code = "BUILD_MISMATCH",
                    Message = message,
                }, "host"));
                return;
            }

            if (_gameStarted)
            {
                Debug.Log("[RelayGameHost] Duplicate join request after game start; resending guest snapshot.");
                _room.PlayerReconnected(1);
                return;
            }

            if (_room.Players[1] != null)
            {
                Debug.Log("[RelayGameHost] Duplicate join request before start; starting or refreshing room.");
                StartGame();
                return;
            }

            // Add guest as seat 1
            int seat = _room.AddPlayer(new PlayerSession
            {
                PlayerId = req.PlayerId,
                PlayerName = req.PlayerName,
                DeckId = req.DeckId,
            });
            if (seat != 1)
            {
                Debug.LogWarning($"[RelayGameHost] Could not seat guest. Seat result: {seat}");
                return;
            }

            // Reconstruct the guest's deck from the card IDs they sent
            _guestDeck = ReconstructDeck(req);
            if (_guestDeck == null)
            {
                Debug.LogWarning("[RelayGameHost] Could not reconstruct guest deck, using host's deck.");
                _guestDeck = _hostDeck;
            }

            // Both players seated — start the game
            StartGame();
        }

        /// <summary>
        /// Reconstructs a DeckData ScriptableObject from the serialized card IDs sent by the guest.
        /// </summary>
        private DeckData ReconstructDeck(JoinRoomRequest req)
        {
            // Prefer the exact list sent by the guest. This keeps cross-platform
            // rooms deterministic even when one install has older prebuilt assets.
            if (req.MainDeck == null || req.MainDeck.Length == 0)
            {
                DeckData selectedDeck = ResolveDeckBySelectionId(req.DeckId);
                return selectedDeck?.CreatePlayableRuntimeCopy();
            }

            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = $"{req.PlayerName}'s Deck";
            deck.element = Enum.TryParse(req.DeckElement, true, out Element element)
                ? element
                : Element.Flame;
            deck.primaryCreatureType = Enum.TryParse(req.DeckArchetype, true, out CreatureType archetype)
                ? archetype
                : CreatureType.Elemental;
            deck.invokerCard = !string.IsNullOrWhiteSpace(req.InvokerCardId)
                ? _cardDb.GetCard(req.InvokerCardId) as InvokerCardData
                : null;

            // Reconstruct main deck entries
            var mainEntries = new System.Collections.Generic.List<DeckEntry>();
            foreach (var entry in req.MainDeck)
            {
                var cardData = _cardDb.GetCard(entry.CardId);
                if (cardData != null)
                    mainEntries.Add(new DeckEntry { card = cardData, count = entry.Count });
                else
                    Debug.LogWarning($"[RelayGameHost] Unknown card ID: {entry.CardId}");
            }
            deck.cards = mainEntries.ToArray();

            // Reconstruct pillar entries
            if (req.PillarDeck != null)
            {
                var pillarEntries = new System.Collections.Generic.List<DeckEntry>();
                foreach (var entry in req.PillarDeck)
                {
                    var cardData = _cardDb.GetCard(entry.CardId);
                    if (cardData != null)
                        pillarEntries.Add(new DeckEntry { card = cardData, count = entry.Count });
                }
                deck.pillars = pillarEntries.ToArray();
            }
            deck.wardIds = req.WardIds;

            DeckData playable = deck.CreatePlayableRuntimeCopy();
            if (!playable.IsValid)
            {
                Debug.LogWarning($"[RelayGameHost] Rejected {req.PlayerName}'s incomplete {playable.TotalMainCards}/{GameConstants.DeckSize} card deck.");
                return null;
            }

            return playable;
        }

        private DeckData ResolveDeckBySelectionId(string deckId)
        {
            if (string.IsNullOrWhiteSpace(deckId))
                return null;

            if (deckId.StartsWith("prebuilt:", StringComparison.OrdinalIgnoreCase))
            {
                string deckName = deckId.Substring("prebuilt:".Length);
                return Resources.LoadAll<DeckData>("CardData/Decks")
                    .FirstOrDefault(deck => deck != null && deck.deckName == deckName);
            }

            PlayerProfile profile = ProfileManager.Load();
            SavedDeck saved = profile?.customDecks?.FirstOrDefault(deck => deck.id == deckId);
            if (saved != null)
                return DeckConverter.ToRuntimeDeck(saved, _cardDb);

            return null;
        }

        private void HandleActionRequest(NetEnvelope envelope)
        {
            if (!_gameStarted) return;

            var req = JsonUtility.FromJson<ActionRequest>(envelope.Payload);
            // Guest is always seat 1
            _room.ProcessAction(1, req.Action, req.SequenceNum);
        }

        private void HandleLeaveRequest()
        {
            _room.PlayerDisconnected(1);
        }

        private void HandleForfeitRequest()
        {
            if (!_gameStarted) return;
            _room.ForfeitPlayer(1);
        }

        private void HandlePing(NetEnvelope envelope)
        {
            var ping = JsonUtility.FromJson<PingMessage>(envelope.Payload);
            _relay.SendMessage(new PongMessage
            {
                ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ClientTime = ping.ClientTime,
            }, "host");
        }

        private void HandleStateAppliedAck(NetEnvelope envelope)
        {
            var ack = JsonUtility.FromJson<StateAppliedAck>(envelope.Payload);
            if (ack == null || ack.PlayerIndex != 1)
                return;

            if (ack.ProtocolVersion != MultiplayerProtocol.CurrentVersion
                || !string.Equals(ack.BuildId, MultiplayerProtocol.BuildId, StringComparison.Ordinal))
            {
                string mismatch = $"Ignored stale guest acknowledgement from {ack.BuildId ?? "unknown"} (protocol {ack.ProtocolVersion}).";
                Debug.LogError($"[RelayGameHost] {mismatch}");
                PublishGuestSyncStatus(mismatch);
                return;
            }

            bool receivedOnly = !string.IsNullOrWhiteSpace(ack.StateKind)
                && ack.StateKind.StartsWith("Received", StringComparison.OrdinalIgnoreCase);
            if (receivedOnly)
            {
                if (ack.ServerSequence > _lastGuestReceivedSequence)
                    _lastGuestReceivedSequence = ack.ServerSequence;
            }
            else
            {
                int expectedHandCount = _room?.Core?.State?.Players != null
                    ? _room.Core.State.Players[1].Hand.Count
                    : -1;
                bool renderComplete = expectedHandCount >= 0
                    && ack.RenderedHandCount == expectedHandCount
                    && ack.RenderedNamedCardCount == expectedHandCount
                    && ack.RenderedArtworkCount == expectedHandCount;
                if (!renderComplete)
                {
                    string incomplete = $"Guest battle view incomplete for state #{ack.ServerSequence}: expected {expectedHandCount} hand cards; rendered {ack.RenderedHandCount}, named {ack.RenderedNamedCardCount}, artwork {ack.RenderedArtworkCount}.";
                    Debug.LogError($"[RelayGameHost] {incomplete}");
                    PublishGuestSyncStatus(incomplete);
                    return;
                }

                if (ack.ServerSequence > _lastGuestAppliedSequence)
                    _lastGuestAppliedSequence = ack.ServerSequence;
            }

            bool synced = GuestSynced;
            string message = synced
                ? receivedOnly
                    ? $"Guest received state #{ack.ServerSequence}; waiting for battle view."
                    : $"Guest synced state #{ack.ServerSequence}."
                : $"Guest applied state #{ack.ServerSequence}; waiting for #{_lastGuestStateSequence}.";

            Debug.Log($"[RelayGameHost] {message}");
            PublishGuestSyncStatus(message);
        }

        private void HandlePrivateStateRequest(NetEnvelope envelope)
        {
            var request = JsonUtility.FromJson<PrivateStateRequest>(envelope.Payload);
            if (!_gameStarted || request == null || request.PlayerIndex != 1)
                return;

            Debug.LogWarning($"[RelayGameHost] Guest requested owner-private resync: {request.Reason}");
            _room.SendStateSnapshot(1);
        }

        /// <summary>
        /// Handle messages FROM the AuthoritativeRoom TO players.
        /// Seat 0 (host) processed locally; Seat 1 (guest) sent via Relay.
        /// </summary>
        private void HandleOutgoingMessage(int seatIndex, NetEnvelope envelope)
        {
            if (seatIndex == 0)
            {
                // Local: process in the host's own game view
                ProcessLocalMessage(envelope);
            }
            else
            {
                // Remote: send to the guest via Relay
                SendRemoteEnvelope(envelope);
                TrackGuestStateEnvelope(envelope);
                if (envelope.Type == nameof(GameOver))
                {
                    if (_gameOverBurst != null)
                        StopCoroutine(_gameOverBurst);
                    _gameOverBurst = StartCoroutine(ResendRemoteEnvelopeBurst(envelope, 5, 0.45f));
                }
            }
        }

        // ═════════════════════════════════════════════════
        //  GAME START
        // ═════════════════════════════════════════════════

        private void StartGame()
        {
            if (_gameStarted) return;

            _room.StartGame(_cardDb, _hostDeck, _guestDeck);
            _gameStarted = true;

            _room.Core.OnGameOver += (winner, reason) =>
            {
                OnGameEnded?.Invoke(winner, reason);
            };

            OnGameStarted?.Invoke();
            Debug.Log("[RelayGameHost] Game started!");

            if (_startSnapshotBurst != null)
                StopCoroutine(_startSnapshotBurst);
            _startSnapshotBurst = StartCoroutine(ResendStartSnapshotBurst());

            if (_stateHeartbeat != null)
                StopCoroutine(_stateHeartbeat);
            _stateHeartbeat = null;

            if (_guestSyncWatchdog != null)
                StopCoroutine(_guestSyncWatchdog);
            _guestSyncWatchdog = StartCoroutine(WatchGuestSync());
        }

        private IEnumerator ResendStartSnapshotBurst()
        {
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForSecondsRealtime(0.6f);
                if (!_gameStarted || _room == null || _room.Finished)
                    yield break;
                if (GuestSynced)
                    break;

                Debug.Log($"[RelayGameHost] Resending guest start snapshot ({i + 1}/10).");
                _room.PlayerReconnected(1);
            }

            _startSnapshotBurst = null;
        }

        // ═════════════════════════════════════════════════
        //  HOST-SIDE ACTION SUBMISSION
        // ═════════════════════════════════════════════════

        /// <summary>
        /// Submit an action as the host (seat 0).
        /// Goes directly to the AuthoritativeRoom — no network needed.
        /// </summary>
        public void SubmitHostAction(GameAction action, int sequenceNum)
        {
            if (!_gameStarted) return;

            var sa = SerializableAction.FromGameAction(action);
            _room.ProcessAction(0, sa, sequenceNum);
        }

        public void SubmitHostForfeit()
        {
            if (!_gameStarted) return;
            _room.ForfeitPlayer(0);
        }

        // ═════════════════════════════════════════════════
        //  LOCAL MESSAGE PROCESSING
        // ═════════════════════════════════════════════════

        /// <summary>Process a message intended for the local host player.</summary>
        public event Action<NetEnvelope> OnLocalMessage;

        private void ProcessLocalMessage(NetEnvelope envelope)
        {
            OnLocalMessage?.Invoke(envelope);
        }

        private void SendRemoteEnvelope(NetEnvelope envelope)
        {
            if (envelope == null || _relay == null)
                return;

            string json = JsonUtility.ToJson(envelope);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            _relay.SendData(bytes);
        }

        private void TrackGuestStateEnvelope(NetEnvelope envelope)
        {
            int sequence = ExtractServerSequence(envelope);
            if (sequence < 0)
                return;

            _lastGuestStateEnvelope = envelope;
            bool wasSynced = GuestSynced;
            int previousSequence = _lastGuestStateSequence;
            _lastGuestStateSequence = sequence;
            if (previousSequence != sequence || wasSynced != GuestSynced)
            {
                PublishGuestSyncStatus(GuestSynced
                    ? $"Guest synced state #{sequence}."
                    : $"Sent state #{sequence}; waiting for guest sync.");
            }
        }

        private static int ExtractServerSequence(NetEnvelope envelope)
        {
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.Payload))
                return -1;

            switch (envelope.Type)
            {
                case nameof(GameStateSnapshot):
                    return JsonUtility.FromJson<GameStateSnapshot>(envelope.Payload)?.ServerSequence ?? -1;
                case nameof(ActionConfirmed):
                    return JsonUtility.FromJson<ActionConfirmed>(envelope.Payload)?.ServerSequence ?? -1;
                case nameof(GameOver):
                    return JsonUtility.FromJson<GameOver>(envelope.Payload)?.ServerSequence ?? -1;
                default:
                    return -1;
            }
        }

        private IEnumerator WatchGuestSync()
        {
            while (_gameStarted && _room != null && !_room.Finished)
            {
                yield return new WaitForSecondsRealtime(1f);
                if (_lastGuestStateEnvelope == null || GuestSynced)
                    continue;

                if (Time.unscaledTime - _lastGuestResendAt < 1.5f)
                    continue;

                _lastGuestResendAt = Time.unscaledTime;
                string message = $"Guest has not confirmed state #{_lastGuestStateSequence}; resending.";
                Debug.LogWarning($"[RelayGameHost] {message}");
                PublishGuestSyncStatus(message);
                SendRemoteEnvelope(_lastGuestStateEnvelope);
            }

            _guestSyncWatchdog = null;
        }

        private void PublishGuestSyncStatus(string message)
        {
            OnGuestSyncStatusChanged?.Invoke(new SyncStatus
            {
                PlayerIndex = 1,
                LastSentServerSequence = _lastGuestStateSequence,
                LastAppliedServerSequence = _lastGuestAppliedSequence,
                Synced = GuestSynced,
                Message = message,
            });
        }

        private IEnumerator ResendRemoteEnvelopeBurst(NetEnvelope envelope, int count, float interval)
        {
            if (envelope == null)
                yield break;

            for (int i = 0; i < count; i++)
            {
                yield return new WaitForSecondsRealtime(interval);
                SendRemoteEnvelope(envelope);
            }
        }
    }
}
