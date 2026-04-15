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
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
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
        private bool _isHost;
        private bool _connected;
        private string _joinCode;

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

        /// <summary>
        /// Initialize Unity Gaming Services and sign in anonymously.
        /// Must be called once before hosting or joining.
        /// </summary>
        public async Task InitializeServices()
        {
            if (UnityServices.State == ServicesInitializationState.Initialized)
                return;

            try
            {
                await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                Debug.Log($"[RelayManager] Signed in as {AuthenticationService.Instance.PlayerId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayManager] Init failed: {ex.Message}");
                OnError?.Invoke($"Failed to initialize services: {ex.Message}");
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

            try
            {
                // Allocate relay for 1 other player (2 total - 1 host = 1 connection)
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
                _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                // Build relay server data
                var relayServerData = new RelayServerData(allocation, "dtls");

                // Create network driver with relay
                var settings = new NetworkSettings();
                settings.WithRelayParameters(ref relayServerData);
                _driver = NetworkDriver.Create(settings);

                // Bind and listen
                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                {
                    OnError?.Invoke("Failed to bind host driver.");
                    return null;
                }
                _driver.Listen();

                Debug.Log($"[RelayManager] Host ready. Join code: {_joinCode}");
                OnJoinCodeCreated?.Invoke(_joinCode);
                return _joinCode;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayManager] Host failed: {ex.Message}");
                OnError?.Invoke($"Failed to create room: {ex.Message}");
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

            try
            {
                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

                var relayServerData = new RelayServerData(joinAllocation, "dtls");

                var settings = new NetworkSettings();
                settings.WithRelayParameters(ref relayServerData);
                _driver = NetworkDriver.Create(settings);

                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                {
                    OnError?.Invoke("Failed to bind client driver.");
                    return false;
                }

                _clientConnection = _driver.Connect();

                Debug.Log("[RelayManager] Client connecting via relay...");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayManager] Join failed: {ex.Message}");
                OnError?.Invoke($"Failed to join: {ex.Message}");
                return false;
            }
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
                        OnDataReceived?.Invoke(bytes);
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
                        OnDataReceived?.Invoke(bytes);
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
            if (!_driver.IsCreated || !_connected) return;

            var connection = _isHost ? _hostConnection : _clientConnection;
            if (!connection.IsCreated) return;

            _driver.BeginSend(connection, out var writer);
            writer.WriteBytes(data);
            _driver.EndSend(writer);
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
        }
    }
}
