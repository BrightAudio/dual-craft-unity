// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Network Client (WebSocket Transport)
//
//  "WebSockets provide persistent, bidirectional
//   communication between the client and server."
//
//  This is the transport layer. It owns the WebSocket
//  connection, serializes/deserializes NetEnvelopes,
//  handles reconnection, and dispatches typed events.
//  Runs on a background thread; events fire on main thread
//  via a concurrent queue polled in Update().
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DualCraft.Networking
{
    /// <summary>
    /// Connection state reported to the UI layer.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
    }

    /// <summary>
    /// WebSocket client that sends/receives <see cref="NetEnvelope"/>
    /// messages. Thread-safe; all public events are dispatched on the
    /// Unity main thread via a concurrent queue polled in Update().
    /// </summary>
    public class NetworkClient : MonoBehaviour
    {
        // ── Configuration ───────────────────────────────
        [Header("Connection")]
        [SerializeField] private string _serverUrl = "ws://localhost:8080/ws";
        [SerializeField] private float _reconnectDelay = 2f;
        [SerializeField] private int _maxReconnectAttempts = 5;
        [SerializeField] private float _heartbeatInterval = 10f;

        // ── State ───────────────────────────────────────
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private ConnectionState _state = ConnectionState.Disconnected;
        private int _reconnectAttempts;
        private float _lastHeartbeat;
        private string _playerId;
        private string _authToken;

        /// <summary>Estimated round-trip latency in ms.</summary>
        public long LatencyMs { get; private set; }

        /// <summary>Current connection state.</summary>
        public ConnectionState State => _state;

        // ── Events (all fired on main thread) ───────────

        public event Action<ConnectionState> OnConnectionStateChanged;
        public event Action<RoomJoined> OnRoomJoined;
        public event Action<WaitingForOpponent> OnWaitingForOpponent;
        public event Action<GameStateSnapshot> OnGameStateSnapshot;
        public event Action<ActionConfirmed> OnActionConfirmed;
        public event Action<ActionRejected> OnActionRejected;
        public event Action<GameOver> OnGameOver;
        public event Action<OpponentDisconnected> OnOpponentDisconnected;
        public event Action<OpponentReconnected> OnOpponentReconnected;
        public event Action<ServerError> OnServerError;

        // ── Thread-safe event queue ─────────────────────
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        // ── Singleton ───────────────────────────────────
        public static NetworkClient Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            // Dispatch queued events on the main thread
            while (_mainThreadQueue.TryDequeue(out var action))
                action?.Invoke();

            // Heartbeat
            if (_state == ConnectionState.Connected)
            {
                _lastHeartbeat += Time.deltaTime;
                if (_lastHeartbeat >= _heartbeatInterval)
                {
                    _lastHeartbeat = 0f;
                    Send(new PingMessage
                    {
                        ClientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });
                }
            }
        }

        private void OnDestroy()
        {
            Disconnect();
            if (Instance == this) Instance = null;
        }

        // ═════════════════════════════════════════════════
        //  PUBLIC API
        // ═════════════════════════════════════════════════

        /// <summary>Set credentials before connecting.</summary>
        public void SetCredentials(string playerId, string authToken)
        {
            _playerId = playerId;
            _authToken = authToken;
        }

        /// <summary>Connect to the game server.</summary>
        public async void Connect(string serverUrl = null)
        {
            if (_state == ConnectionState.Connected || _state == ConnectionState.Connecting)
                return;

            if (!string.IsNullOrEmpty(serverUrl))
                _serverUrl = serverUrl;

            await ConnectInternal();
        }

        /// <summary>Gracefully disconnect.</summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try { _ = _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Player disconnected", CancellationToken.None); }
                catch { /* already closed */ }
            }
            _ws?.Dispose();
            _ws = null;
            SetState(ConnectionState.Disconnected);
            _reconnectAttempts = 0;
        }

        /// <summary>Send any message to the server.</summary>
        public void Send<T>(T message) where T : class
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
            {
                Debug.LogWarning("[NetworkClient] Cannot send — not connected.");
                return;
            }

            var envelope = NetEnvelope.Create(message, _playerId);
            string json = JsonUtility.ToJson(envelope);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            _ = _ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _cts?.Token ?? CancellationToken.None
            );
        }

        /// <summary>Join a room (or enter matchmaking if roomId empty).</summary>
        public void JoinRoom(string roomId, string deckId)
        {
            Send(new JoinRoomRequest
            {
                PlayerId = _playerId,
                PlayerName = _playerId, // server looks up display name
                RoomId = roomId,
                DeckId = deckId,
                AuthToken = _authToken,
            });
        }

        /// <summary>Create a private room.</summary>
        public void CreateRoom(string deckId, RoomSettings settings)
        {
            Send(new CreateRoomRequest
            {
                PlayerId = _playerId,
                PlayerName = _playerId,
                DeckId = deckId,
                AuthToken = _authToken,
                Settings = settings,
            });
        }

        /// <summary>Submit a game action.</summary>
        public void SendAction(int playerIndex, Battle.GameAction action, int sequenceNum)
        {
            Send(new ActionRequest
            {
                PlayerId = _playerId,
                PlayerIndex = playerIndex,
                SequenceNum = sequenceNum,
                Action = SerializableAction.FromGameAction(action),
            });
        }

        /// <summary>Leave the current game.</summary>
        public void LeaveGame(string roomId)
        {
            Send(new LeaveRequest { PlayerId = _playerId, RoomId = roomId });
        }

        /// <summary>Request a rematch.</summary>
        public void RequestRematch(string roomId)
        {
            Send(new RematchRequest { PlayerId = _playerId, RoomId = roomId });
        }

        // ═════════════════════════════════════════════════
        //  INTERNALS
        // ═════════════════════════════════════════════════

        private async Task ConnectInternal()
        {
            SetState(ConnectionState.Connecting);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ws?.Dispose();
            _ws = new ClientWebSocket();

            try
            {
                await _ws.ConnectAsync(new Uri(_serverUrl), _cts.Token);
                SetState(ConnectionState.Connected);
                _reconnectAttempts = 0;
                _ = ReceiveLoop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkClient] Connect failed: {ex.Message}");
                HandleDisconnect();
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];

            try
            {
                while (_ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _ws.ReceiveAsync(
                            new ArraySegment<byte>(buffer), _cts.Token);
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        HandleDisconnect();
                        return;
                    }

                    string json = sb.ToString();
                    ProcessMessage(json);
                }
            }
            catch (OperationCanceledException) { /* intentional disconnect */ }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkClient] Receive error: {ex.Message}");
                HandleDisconnect();
            }
        }

        private void ProcessMessage(string json)
        {
            NetEnvelope envelope;
            try { envelope = JsonUtility.FromJson<NetEnvelope>(json); }
            catch { Debug.LogWarning("[NetworkClient] Failed to parse envelope."); return; }

            switch (envelope.Type)
            {
                case nameof(RoomJoined):
                    Enqueue(() => OnRoomJoined?.Invoke(
                        JsonUtility.FromJson<RoomJoined>(envelope.Payload)));
                    break;

                case nameof(WaitingForOpponent):
                    Enqueue(() => OnWaitingForOpponent?.Invoke(
                        JsonUtility.FromJson<WaitingForOpponent>(envelope.Payload)));
                    break;

                case nameof(GameStateSnapshot):
                    Enqueue(() => OnGameStateSnapshot?.Invoke(
                        JsonUtility.FromJson<GameStateSnapshot>(envelope.Payload)));
                    break;

                case nameof(ActionConfirmed):
                    Enqueue(() => OnActionConfirmed?.Invoke(
                        JsonUtility.FromJson<ActionConfirmed>(envelope.Payload)));
                    break;

                case nameof(ActionRejected):
                    Enqueue(() => OnActionRejected?.Invoke(
                        JsonUtility.FromJson<ActionRejected>(envelope.Payload)));
                    break;

                case nameof(GameOver):
                    Enqueue(() => OnGameOver?.Invoke(
                        JsonUtility.FromJson<GameOver>(envelope.Payload)));
                    break;

                case nameof(OpponentDisconnected):
                    Enqueue(() => OnOpponentDisconnected?.Invoke(
                        JsonUtility.FromJson<OpponentDisconnected>(envelope.Payload)));
                    break;

                case nameof(OpponentReconnected):
                    Enqueue(() => OnOpponentReconnected?.Invoke(
                        JsonUtility.FromJson<OpponentReconnected>(envelope.Payload)));
                    break;

                case nameof(PongMessage):
                    var pong = JsonUtility.FromJson<PongMessage>(envelope.Payload);
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    LatencyMs = now - pong.ClientTime;
                    break;

                case nameof(ServerError):
                    Enqueue(() => OnServerError?.Invoke(
                        JsonUtility.FromJson<ServerError>(envelope.Payload)));
                    break;

                default:
                    Debug.LogWarning($"[NetworkClient] Unknown message type: {envelope.Type}");
                    break;
            }
        }

        private void HandleDisconnect()
        {
            if (_state == ConnectionState.Disconnected) return;

            if (_reconnectAttempts < _maxReconnectAttempts)
            {
                _reconnectAttempts++;
                SetState(ConnectionState.Reconnecting);
                _ = ReconnectAfterDelay();
            }
            else
            {
                SetState(ConnectionState.Disconnected);
                Enqueue(() => OnServerError?.Invoke(new ServerError
                {
                    Code = "DISCONNECTED",
                    Message = "Connection lost after max reconnect attempts.",
                }));
            }
        }

        private async Task ReconnectAfterDelay()
        {
            try
            {
                await Task.Delay((int)(_reconnectDelay * 1000 * _reconnectAttempts), _cts.Token);
                await ConnectInternal();
            }
            catch (OperationCanceledException) { /* intentional */ }
        }

        private void SetState(ConnectionState newState)
        {
            if (_state == newState) return;
            _state = newState;
            Enqueue(() => OnConnectionStateChanged?.Invoke(_state));
        }

        private void Enqueue(Action action) => _mainThreadQueue.Enqueue(action);
    }
}
