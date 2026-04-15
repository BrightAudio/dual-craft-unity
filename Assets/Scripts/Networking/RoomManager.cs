// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Room Manager (Matchmaking & Lobbies)
//
//  "A fair game experience depends on skill-based
//   matchmaking, anti-collusion policies, and elastic
//   room management."
//
//  Client-side room state tracking and matchmaking UI
//  helper. The actual matchmaking runs on the server;
//  this class manages the local session state and
//  surfaces events for the UI layer.
// ═══════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace DualCraft.Networking
{
    /// <summary>
    /// Current matchmaking / room status visible to the UI.
    /// </summary>
    public enum MatchState
    {
        Idle,
        Searching,
        InLobby,
        InGame,
    }

    /// <summary>
    /// Local room session data tracked on the client.
    /// </summary>
    [Serializable]
    public class RoomSession
    {
        public string RoomId;
        public int PlayerIndex;
        public string OpponentName;
        public RoomSettings Settings;
        public MatchState State;
    }

    /// <summary>
    /// Manages the client's room lifecycle: searching → lobby → in-game → idle.
    /// Listens to <see cref="NetworkClient"/> events and maintains
    /// a <see cref="RoomSession"/> that the UI can bind to.
    /// </summary>
    public class RoomManager : MonoBehaviour
    {
        // ── State ───────────────────────────────────────
        public RoomSession CurrentSession { get; private set; } = new();

        // ── Events ──────────────────────────────────────
        public event Action<MatchState> OnMatchStateChanged;
        public event Action<RoomSession> OnRoomReady;
        public event Action<string> OnMatchError;
        public event Action<string> OnOpponentLeft;
        public event Action OnOpponentReturned;

        // ── Singleton ───────────────────────────────────
        public static RoomManager Instance { get; private set; }

        private NetworkClient _net;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            _net = NetworkClient.Instance;
            if (_net == null)
            {
                Debug.LogError("[RoomManager] NetworkClient not found.");
                return;
            }

            _net.OnRoomJoined += HandleRoomJoined;
            _net.OnWaitingForOpponent += HandleWaiting;
            _net.OnGameOver += HandleGameOver;
            _net.OnOpponentDisconnected += HandleOpponentDisconnect;
            _net.OnOpponentReconnected += HandleOpponentReconnect;
            _net.OnServerError += HandleError;
        }

        private void OnDestroy()
        {
            if (_net != null)
            {
                _net.OnRoomJoined -= HandleRoomJoined;
                _net.OnWaitingForOpponent -= HandleWaiting;
                _net.OnGameOver -= HandleGameOver;
                _net.OnOpponentDisconnected -= HandleOpponentDisconnect;
                _net.OnOpponentReconnected -= HandleOpponentReconnect;
                _net.OnServerError -= HandleError;
            }
            if (Instance == this) Instance = null;
        }

        // ═════════════════════════════════════════════════
        //  PUBLIC API — called from UI
        // ═════════════════════════════════════════════════

        /// <summary>Enter matchmaking queue.</summary>
        public void FindMatch(string deckId)
        {
            if (_net.State != ConnectionState.Connected)
            {
                OnMatchError?.Invoke("Not connected to server.");
                return;
            }

            SetState(MatchState.Searching);
            _net.JoinRoom("", deckId);
        }

        /// <summary>Join a specific room by code.</summary>
        public void JoinRoom(string roomId, string deckId)
        {
            if (_net.State != ConnectionState.Connected)
            {
                OnMatchError?.Invoke("Not connected to server.");
                return;
            }

            SetState(MatchState.Searching);
            _net.JoinRoom(roomId, deckId);
        }

        /// <summary>Create a private room.</summary>
        public void CreatePrivateRoom(string deckId, RoomSettings settings = null)
        {
            if (_net.State != ConnectionState.Connected)
            {
                OnMatchError?.Invoke("Not connected to server.");
                return;
            }

            settings ??= new RoomSettings
            {
                TurnTimerSeconds = 90,
                AllowSpectators = false,
                RankedMatch = false,
                GameMode = "standard",
            };

            SetState(MatchState.InLobby);
            _net.CreateRoom(deckId, settings);
        }

        /// <summary>Leave current room/game.</summary>
        public void Leave()
        {
            if (!string.IsNullOrEmpty(CurrentSession.RoomId))
                _net.LeaveGame(CurrentSession.RoomId);

            ResetSession();
        }

        /// <summary>Cancel matchmaking search.</summary>
        public void CancelSearch()
        {
            if (CurrentSession.State == MatchState.Searching)
            {
                _net.LeaveGame("");
                ResetSession();
            }
        }

        // ═════════════════════════════════════════════════
        //  EVENT HANDLERS
        // ═════════════════════════════════════════════════

        private void HandleRoomJoined(RoomJoined msg)
        {
            CurrentSession.RoomId = msg.RoomId;
            CurrentSession.PlayerIndex = msg.PlayerIndex;
            CurrentSession.OpponentName = msg.OpponentName;
            CurrentSession.Settings = msg.Settings;

            if (msg.GameStarted)
            {
                SetState(MatchState.InGame);
                OnRoomReady?.Invoke(CurrentSession);
            }
            else
            {
                SetState(MatchState.InLobby);
            }
        }

        private void HandleWaiting(WaitingForOpponent msg)
        {
            CurrentSession.RoomId = msg.RoomId;
            SetState(MatchState.InLobby);
        }

        private void HandleGameOver(GameOver msg)
        {
            SetState(MatchState.Idle);
        }

        private void HandleOpponentDisconnect(OpponentDisconnected msg)
        {
            OnOpponentLeft?.Invoke(msg.Message);
        }

        private void HandleOpponentReconnect(OpponentReconnected msg)
        {
            OnOpponentReturned?.Invoke();
        }

        private void HandleError(ServerError err)
        {
            OnMatchError?.Invoke($"[{err.Code}] {err.Message}");
            if (CurrentSession.State == MatchState.Searching)
                ResetSession();
        }

        // ── Helpers ─────────────────────────────────────

        private void SetState(MatchState state)
        {
            if (CurrentSession.State == state) return;
            CurrentSession.State = state;
            OnMatchStateChanged?.Invoke(state);
        }

        private void ResetSession()
        {
            CurrentSession = new RoomSession();
            SetState(MatchState.Idle);
        }
    }
}
