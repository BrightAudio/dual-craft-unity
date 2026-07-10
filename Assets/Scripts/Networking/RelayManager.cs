// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Relay Manager (Unity Relay Integration)
//
//  Wraps Unity Relay so two players can connect peer-to-peer
//  without port forwarding. The host allocates a relay,
//  gets a join code, shares it with a friend, and they
//  connect through Unity's free relay infrastructure.
//
//  This replaces the raw WebSocket transport for online play.
//  The host also runs AuthoritativeRoom locally.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
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
    /// <summary>
    /// Manages Unity Relay connections for host and client.
    /// Singleton — persists across scenes.
    /// </summary>
    public class RelayManager : MonoBehaviour
    {
        public static RelayManager Instance { get; private set; }

        // ── State ───────────────────────────────────────
        private NetworkDriver _driver;
        private NetworkConnection _clientConnection;   // client's connection to host
        private NetworkConnection _hostConnection;     // host's accepted client connection
        private NetworkPipeline _reliablePipeline;
        private bool _isHost;
        private bool _connected;
        private string _joinCode;
        private readonly Dictionary<string, ChunkAccumulator> _incomingChunks = new();

        private const string ChunkMagic = "DUALMON_CHUNK_V1";
        private const int ReliableFragmentPayloadCapacity = 64 * 1024;

        // ── Events ──────────────────────────────────────
        public event Action<string> OnJoinCodeCreated;  // host gets this to share
        public event Action OnClientConnected;          // both sides fire this
        public event Action OnClientDisconnected;
        public event Action<byte[]> OnDataReceived;     // raw bytes received
        public event Action<string> OnError;

        /// <summary>The join code friends use to connect.</summary>
        public string JoinCode => _joinCode;
        public bool IsHost => _isHost;
        public bool IsConnected => _connected;
        public string LastStatus { get; private set; } = "";
        public string LastError { get; private set; } = "";
        public bool ServicesReady { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            Shutdown();
            if (Instance == this) Instance = null;
        }

        [Serializable]
        private class RelayChunk
        {
            public string Magic;
            public string MessageId;
            public int Index;
            public int Total;
            public string Payload;
        }

        private class ChunkAccumulator
        {
            public readonly string[] Chunks;
            public int Received;

            public ChunkAccumulator(int total)
            {
                Chunks = new string[Mathf.Max(1, total)];
            }
        }

        /// <summary>
        /// Initialize Unity Gaming Services and sign in anonymously.
        /// Must be called once before hosting or joining.
        /// </summary>
        public async Task InitializeServices()
        {
            LastError = "";
            LastStatus = "Checking Unity Services...";

            if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
            {
                ServicesReady = false;
                LastError = "Unity Services is not linked to this project. In Unity, connect Project Settings > Services, enable Authentication and Relay, then rebuild.";
                Debug.LogError($"[RelayManager] {LastError}");
                OnError?.Invoke(LastError);
                return;
            }

            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    LastStatus = "Signing in anonymously...";
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                ServicesReady = true;
                return;
            }

            try
            {
                LastStatus = "Initializing Unity Services...";
                await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    LastStatus = "Signing in anonymously...";
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                Debug.Log($"[RelayManager] Signed in as {AuthenticationService.Instance.PlayerId}");
                ServicesReady = true;
                LastStatus = "Unity Relay ready.";
            }
            catch (Exception ex)
            {
                ServicesReady = false;
                LastError = FriendlyRelayError("Failed to initialize Unity Services", ex);
                Debug.LogError($"[RelayManager] Init failed: {ex}");
                OnError?.Invoke(LastError);
            }
        }

        // ═════════════════════════════════════════════════
        //  HOST — Create a relay and get a join code
        // ═════════════════════════════════════════════════

        /// <summary>
        /// Allocate a Relay server and start listening for a client.
        /// Returns the join code that the friend enters.
        /// </summary>
        public async Task<string> StartHost()
        {
            _isHost = true;
            LastError = "";

            try
            {
                await InitializeServices();
                if (!ServicesReady)
                    return null;

                LastStatus = "Creating Relay room...";

                // Allocate relay for 1 other player (2 total - 1 host = 1 connection)
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
                LastStatus = "Fetching room code...";
                _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                // Build relay server data
                var relayServerData = new RelayServerData(allocation, "dtls");

                // Create network driver with relay
                var settings = new NetworkSettings();
                settings.WithRelayParameters(ref relayServerData);
                settings.WithFragmentationStageParameters(ReliableFragmentPayloadCapacity);
                settings.WithReliableStageParameters(windowSize: 64, minimumResendTime: 64, maximumResendTime: 500);
                _driver = NetworkDriver.Create(settings);
                _reliablePipeline = _driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));

                // Bind and listen
                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                {
                    OnError?.Invoke("Failed to bind host driver.");
                    return null;
                }
                _driver.Listen();

                Debug.Log($"[RelayManager] Host ready. Join code: {_joinCode}");
                LastStatus = "Relay room ready.";
                OnJoinCodeCreated?.Invoke(_joinCode);
                return _joinCode;
            }
            catch (Exception ex)
            {
                LastError = FriendlyRelayError("Failed to create Relay room", ex);
                Debug.LogError($"[RelayManager] Host failed: {ex}");
                OnError?.Invoke(LastError);
                return null;
            }
        }

        // ═════════════════════════════════════════════════
        //  CLIENT — Join using a code from a friend
        // ═════════════════════════════════════════════════

        /// <summary>
        /// Join a relay using a friend's join code.
        /// </summary>
        public async Task<bool> JoinGame(string joinCode)
        {
            _isHost = false;
            _joinCode = joinCode;
            LastError = "";

            try
            {
                await InitializeServices();
                if (!ServicesReady)
                    return false;

                LastStatus = "Joining Relay room...";
                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

                var relayServerData = new RelayServerData(joinAllocation, "dtls");

                var settings = new NetworkSettings();
                settings.WithRelayParameters(ref relayServerData);
                settings.WithFragmentationStageParameters(ReliableFragmentPayloadCapacity);
                settings.WithReliableStageParameters(windowSize: 64, minimumResendTime: 64, maximumResendTime: 500);
                _driver = NetworkDriver.Create(settings);
                _reliablePipeline = _driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));

                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                {
                    OnError?.Invoke("Failed to bind client driver.");
                    return false;
                }

                _clientConnection = _driver.Connect();

                Debug.Log("[RelayManager] Client connecting via relay...");
                LastStatus = "Connecting to host...";
                return true;
            }
            catch (Exception ex)
            {
                LastError = FriendlyRelayError("Failed to join Relay room", ex);
                Debug.LogError($"[RelayManager] Join failed: {ex}");
                OnError?.Invoke(LastError);
                return false;
            }
        }

        private static string FriendlyRelayError(string prefix, Exception ex)
        {
            string detail = ex?.Message;
            if (string.IsNullOrWhiteSpace(detail))
                detail = ex?.GetType().Name ?? "Unknown error";

            return $"{prefix}: {detail}";
        }

        // ═════════════════════════════════════════════════
        //  UPDATE — Pump network events
        // ═════════════════════════════════════════════════

        private void Update()
        {
            if (!_driver.IsCreated) return;

            _driver.ScheduleUpdate().Complete();

            if (_isHost)
                PumpHost();
            else
                PumpClient();
        }

        private void PumpHost()
        {
            // Accept new connections
            NetworkConnection incoming;
            while ((incoming = _driver.Accept()) != default)
            {
                _hostConnection = incoming;
                _connected = true;
                Debug.Log("[RelayManager] Client connected to host.");
                OnClientConnected?.Invoke();
            }

            if (!_hostConnection.IsCreated) return;

            // Read events
            DataStreamReader reader;
            NetworkEvent.Type evt;
            while ((evt = _driver.PopEventForConnection(_hostConnection, out reader)) != NetworkEvent.Type.Empty)
            {
                switch (evt)
                {
                    case NetworkEvent.Type.Data:
                        var bytes = new byte[reader.Length];
                        reader.ReadBytes(bytes);
                        HandleReceivedBytes(bytes);
                        break;

                    case NetworkEvent.Type.Disconnect:
                        _connected = false;
                        _hostConnection = default;
                        Debug.Log("[RelayManager] Client disconnected.");
                        OnClientDisconnected?.Invoke();
                        break;
                }
            }
        }

        private void PumpClient()
        {
            if (!_clientConnection.IsCreated) return;

            DataStreamReader reader;
            NetworkEvent.Type evt;
            while ((evt = _driver.PopEventForConnection(_clientConnection, out reader)) != NetworkEvent.Type.Empty)
            {
                switch (evt)
                {
                    case NetworkEvent.Type.Connect:
                        _connected = true;
                        Debug.Log("[RelayManager] Connected to host via relay.");
                        OnClientConnected?.Invoke();
                        break;

                    case NetworkEvent.Type.Data:
                        var bytes = new byte[reader.Length];
                        reader.ReadBytes(bytes);
                        HandleReceivedBytes(bytes);
                        break;

                    case NetworkEvent.Type.Disconnect:
                        _connected = false;
                        _clientConnection = default;
                        Debug.Log("[RelayManager] Disconnected from host.");
                        OnClientDisconnected?.Invoke();
                        break;
                }
            }
        }

        // ═════════════════════════════════════════════════
        //  SEND DATA
        // ═════════════════════════════════════════════════

        /// <summary>Send raw bytes to the other player.</summary>
        public void SendData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return;

            SendRawPacket(data);
        }

        private void SendRawPacket(byte[] data)
        {
            if (!_driver.IsCreated || !_connected)
            {
                Debug.LogWarning("[RelayManager] Tried to send before Relay connection was ready.");
                return;
            }

            var connection = _isHost ? _hostConnection : _clientConnection;
            if (!connection.IsCreated)
            {
                Debug.LogWarning("[RelayManager] Tried to send without an active Relay connection.");
                return;
            }

            int beginResult = _driver.BeginSend(_reliablePipeline, connection, out var writer, data.Length);
            if (beginResult != 0)
            {
                string msg = $"Relay send failed before writing payload. Error {beginResult}, bytes {data.Length}.";
                LastError = msg;
                Debug.LogWarning($"[RelayManager] {msg}");
                OnError?.Invoke(msg);
                return;
            }

            writer.WriteBytes(data);
            int endResult = _driver.EndSend(writer);
            if (endResult < 0)
            {
                string msg = $"Relay send failed after writing payload. Error {endResult}, bytes {data.Length}.";
                LastError = msg;
                Debug.LogWarning($"[RelayManager] {msg}");
                OnError?.Invoke(msg);
            }
        }

        private void HandleReceivedBytes(byte[] bytes)
        {
            if (TryHandleChunk(bytes))
                return;

            OnDataReceived?.Invoke(bytes);
        }

        private bool TryHandleChunk(byte[] bytes)
        {
            string json;
            try
            {
                json = Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(json) || !json.Contains(ChunkMagic))
                return false;

            RelayChunk chunk;
            try
            {
                chunk = JsonUtility.FromJson<RelayChunk>(json);
            }
            catch
            {
                return false;
            }

            if (chunk == null
                || chunk.Magic != ChunkMagic
                || string.IsNullOrWhiteSpace(chunk.MessageId)
                || string.IsNullOrWhiteSpace(chunk.Payload)
                || chunk.Total <= 0
                || chunk.Index < 0
                || chunk.Index >= chunk.Total)
            {
                return false;
            }

            if (!_incomingChunks.TryGetValue(chunk.MessageId, out var accumulator)
                || accumulator.Chunks.Length != chunk.Total)
            {
                accumulator = new ChunkAccumulator(chunk.Total);
                _incomingChunks[chunk.MessageId] = accumulator;
            }

            if (accumulator.Chunks[chunk.Index] == null)
            {
                accumulator.Chunks[chunk.Index] = chunk.Payload;
                accumulator.Received++;
            }

            if (accumulator.Received < accumulator.Chunks.Length)
                return true;

            try
            {
                int totalLength = 0;
                byte[][] decoded = new byte[accumulator.Chunks.Length][];
                for (int i = 0; i < accumulator.Chunks.Length; i++)
                {
                    decoded[i] = Convert.FromBase64String(accumulator.Chunks[i]);
                    totalLength += decoded[i].Length;
                }

                byte[] full = new byte[totalLength];
                int offset = 0;
                for (int i = 0; i < decoded.Length; i++)
                {
                    Buffer.BlockCopy(decoded[i], 0, full, offset, decoded[i].Length);
                    offset += decoded[i].Length;
                }

                _incomingChunks.Remove(chunk.MessageId);
                Debug.Log($"[RelayManager] Reassembled {totalLength} byte Relay message from {decoded.Length} chunks.");
                OnDataReceived?.Invoke(full);
            }
            catch (Exception ex)
            {
                _incomingChunks.Remove(chunk.MessageId);
                Debug.LogWarning($"[RelayManager] Failed to reassemble Relay chunks: {ex.Message}");
            }

            return true;
        }

        /// <summary>Send a serialized message envelope.</summary>
        public void SendMessage<T>(T message, string senderId) where T : class
        {
            var envelope = NetEnvelope.Create(message, senderId);
            string json = JsonUtility.ToJson(envelope);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            SendData(bytes);
        }

        // ═════════════════════════════════════════════════
        //  SHUTDOWN
        // ═════════════════════════════════════════════════

        public void Shutdown()
        {
            if (_driver.IsCreated)
            {
                if (_hostConnection.IsCreated) _driver.Disconnect(_hostConnection);
                if (_clientConnection.IsCreated) _driver.Disconnect(_clientConnection);
                _driver.Dispose();
            }
            _connected = false;
            _isHost = false;
            _joinCode = null;
            _incomingChunks.Clear();
        }
    }
}
