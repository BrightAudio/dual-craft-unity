// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Networked Battle Controller (Unity Glue)
//
//  The bridge between the networking layer and the
//  existing battle UI. In online mode, this replaces
//  the local BattleSceneController's game loop:
//
//   - Sends player actions via NetworkClient
//   - Receives authoritative state from the server
//   - Applies state to the visual BattleBoardView
//   - Handles disconnection, reconnection, latency UI
//
//  In offline mode, it wraps a local AuthoritativeRoom
//  so the same code path runs both modes.
// ═══════════════════════════════════════════════════════

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DualCraft.Networking
{
    using Battle;
    using Core;
    using UI.Visual;

    /// <summary>
    /// Whether a match is running over the network or locally.
    /// </summary>
    public enum PlayMode
    {
        Offline,    // local AuthoritativeRoom, no network
        Online,     // remote server via NetworkClient
    }

    /// <summary>
    /// Unity MonoBehaviour that glues the network layer to the
    /// battle view. Attach to the battle scene root.
    /// </summary>
    public class NetworkedBattleController : MonoBehaviour
    {
        // ── Inspector Refs ──────────────────────────────
        [Header("View")]
        [SerializeField] private BattleBoardView boardView;

        [Header("Connection UI")]
        [SerializeField] private GameObject connectionOverlay;
        [SerializeField] private TMP_Text connectionStatusText;
        [SerializeField] private TMP_Text latencyText;
        [SerializeField] private GameObject disconnectBanner;

        [Header("Settings")]
        [SerializeField] private PlayMode playMode = PlayMode.Offline;

        // ── State ───────────────────────────────────────
        private NetworkClient _net;
        private RoomManager _room;
        private int _localPlayerIndex;
        private int _actionSequence;
        private SerializableGameState _currentState;
        private bool _gameActive;

        // ── Offline host (used in PlayMode.Offline) ─────
        private AuthoritativeRoom _offlineRoom;

        // ── Events for external UI (lobby, game-over screen) ─
        public event Action OnGameStarted;
        public event Action<int, string> OnGameEnded;  // winnerIndex, reason
        public event Action<string> OnError;

        private void Start()
        {
            _net = NetworkClient.Instance;
            _room = RoomManager.Instance;

            if (playMode == PlayMode.Online && _net != null)
            {
                BindNetworkEvents();
            }
        }

        private void Update()
        {
            // Show latency in online mode
            if (playMode == PlayMode.Online && _net != null && latencyText != null)
            {
                latencyText.text = $"{_net.LatencyMs}ms";
            }
        }

        private void OnDestroy()
        {
            UnbindNetworkEvents();
        }

        // ═════════════════════════════════════════════════
        //  OFFLINE MODE (LOCAL HOST)
        // ═════════════════════════════════════════════════

        /// <summary>
        /// Start an offline game against AI or local player 2.
        /// Uses the same authoritative model — server = local process.
        /// </summary>
        public void StartOfflineGame(Cards.CardDatabase cardDb,
                                     Cards.DeckData deck0, Cards.DeckData deck1,
                                     string p0Name, string p1Name)
        {
            playMode = PlayMode.Offline;

            _offlineRoom = new AuthoritativeRoom(
                Guid.NewGuid().ToString(),
                new RoomSettings { TurnTimerSeconds = 0, GameMode = "standard" }
            );

            _offlineRoom.AddPlayer(new PlayerSession
            {
                PlayerId = "local_0", PlayerName = p0Name, DeckId = "deck0"
            });
            _offlineRoom.AddPlayer(new PlayerSession
            {
                PlayerId = "local_1", PlayerName = p1Name, DeckId = "deck1"
            });

            // Wire offline room events directly
            _offlineRoom.OnSendToPlayer += HandleOfflineMessage;

            _localPlayerIndex = 0;
            _actionSequence = 0;

            _offlineRoom.StartGame(cardDb, deck0, deck1);
            _gameActive = true;
            OnGameStarted?.Invoke();
        }

        /// <summary>
        /// Submit an action in offline mode.
        /// Routes through the local AuthoritativeRoom.
        /// </summary>
        public void SubmitOfflineAction(GameAction action)
        {
            if (!_gameActive || _offlineRoom == null) return;

            _actionSequence++;
            var sa = SerializableAction.FromGameAction(action);
            _offlineRoom.ProcessAction(_localPlayerIndex, sa, _actionSequence);
        }

        /// <summary>
        /// Handle messages from the offline authoritative room
        /// as if they came from the server.
        /// </summary>
        private void HandleOfflineMessage(int playerIndex, NetEnvelope envelope)
        {
            if (playerIndex != _localPlayerIndex) return;

            switch (envelope.Type)
            {
                case nameof(GameStateSnapshot):
                    var snap = JsonUtility.FromJson<GameStateSnapshot>(envelope.Payload);
                    ApplyState(snap.State);
                    break;

                case nameof(ActionConfirmed):
                    var confirmed = JsonUtility.FromJson<ActionConfirmed>(envelope.Payload);
                    if (confirmed.Success)
                        ApplyState(confirmed.State);
                    break;

                case nameof(ActionRejected):
                    var rejected = JsonUtility.FromJson<ActionRejected>(envelope.Payload);
                    OnError?.Invoke($"Action rejected: {rejected.Reason}");
                    break;

                case nameof(GameOver):
                    var over = JsonUtility.FromJson<GameOver>(envelope.Payload);
                    HandleGameOverState(over);
                    break;
            }
        }

        // ═════════════════════════════════════════════════
        //  ONLINE MODE (NETWORK)
        // ═════════════════════════════════════════════════

        /// <summary>Submit an action in online mode.</summary>
        public void SubmitOnlineAction(GameAction action)
        {
            if (!_gameActive || _net == null) return;

            _actionSequence++;
            _net.SendAction(_localPlayerIndex, action, _actionSequence);
        }

        /// <summary>
        /// Generic submit — picks the right mode automatically.
        /// </summary>
        public void SubmitAction(GameAction action)
        {
            if (playMode == PlayMode.Offline)
                SubmitOfflineAction(action);
            else
                SubmitOnlineAction(action);
        }

        // ── Network event binding ───────────────────────

        private void BindNetworkEvents()
        {
            if (_net == null) return;

            _net.OnConnectionStateChanged += HandleConnectionState;
            _net.OnGameStateSnapshot += HandleSnapshot;
            _net.OnActionConfirmed += HandleActionConfirmed;
            _net.OnActionRejected += HandleActionRejected;
            _net.OnGameOver += HandleGameOverNetwork;
            _net.OnOpponentDisconnected += HandleOpponentDisconnect;
            _net.OnOpponentReconnected += HandleOpponentReconnect;
            _net.OnServerError += HandleServerError;

            if (_room != null)
            {
                _room.OnRoomReady += HandleRoomReady;
            }
        }

        private void UnbindNetworkEvents()
        {
            if (_net != null)
            {
                _net.OnConnectionStateChanged -= HandleConnectionState;
                _net.OnGameStateSnapshot -= HandleSnapshot;
                _net.OnActionConfirmed -= HandleActionConfirmed;
                _net.OnActionRejected -= HandleActionRejected;
                _net.OnGameOver -= HandleGameOverNetwork;
                _net.OnOpponentDisconnected -= HandleOpponentDisconnect;
                _net.OnOpponentReconnected -= HandleOpponentReconnect;
                _net.OnServerError -= HandleServerError;
            }

            if (_room != null)
            {
                _room.OnRoomReady -= HandleRoomReady;
            }
        }

        // ── Network event handlers ──────────────────────

        private void HandleConnectionState(ConnectionState state)
        {
            if (connectionStatusText != null)
                connectionStatusText.text = state.ToString();

            if (connectionOverlay != null)
                connectionOverlay.SetActive(state != ConnectionState.Connected);
        }

        private void HandleRoomReady(RoomSession session)
        {
            _localPlayerIndex = session.PlayerIndex;
            _actionSequence = 0;
            _gameActive = true;
            OnGameStarted?.Invoke();
        }

        private void HandleSnapshot(GameStateSnapshot snap)
        {
            _localPlayerIndex = snap.YourPlayerIndex;
            ApplyState(snap.State);
        }

        private void HandleActionConfirmed(ActionConfirmed msg)
        {
            if (msg.Success)
                ApplyState(msg.State);
        }

        private void HandleActionRejected(ActionRejected msg)
        {
            OnError?.Invoke($"Action rejected: {msg.Reason}");
        }

        private void HandleGameOverNetwork(GameOver msg)
        {
            HandleGameOverState(msg);
        }

        private void HandleOpponentDisconnect(OpponentDisconnected msg)
        {
            if (disconnectBanner != null)
            {
                disconnectBanner.SetActive(true);
                var text = disconnectBanner.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = msg.Message;
            }
        }

        private void HandleOpponentReconnect(OpponentReconnected msg)
        {
            if (disconnectBanner != null)
                disconnectBanner.SetActive(false);
        }

        private void HandleServerError(ServerError err)
        {
            OnError?.Invoke($"[{err.Code}] {err.Message}");
        }

        // ═════════════════════════════════════════════════
        //  STATE APPLICATION — The visual rendering layer
        // ═════════════════════════════════════════════════

        /// <summary>
        /// Apply a server-authoritative state snapshot to the view.
        /// This is where the "clients only render what the server
        /// confirms" principle materializes.
        /// </summary>
        private void ApplyState(SerializableGameState state)
        {
            if (state == null) return;
            _currentState = state;

            if (boardView == null) return;

            // Determine which player data is "ours" vs "theirs"
            var myState = state.Players[_localPlayerIndex];
            var theirState = state.Players[1 - _localPlayerIndex];

            // Player stats
            boardView.SetPlayerName(myState.Name);
            boardView.SetOpponentName(theirState.Name);
            boardView.SetPlayerHP(myState.ConjurorHp, myState.ConjurorMaxHp);
            boardView.SetOpponentHP(theirState.ConjurorHp, theirState.ConjurorMaxHp);
            boardView.SetPlayerWill(myState.Will, myState.MaxWill);
            boardView.SetOpponentWill(theirState.Will, theirState.MaxWill);
            boardView.SetPlayerDeckCount(myState.DeckCount);
            boardView.SetOpponentDeckCount(theirState.DeckCount);

            // Turn info
            boardView.SetTurnCount(state.TurnNumber);
            bool isMyTurn = state.CurrentPlayer == _localPlayerIndex;
            boardView.SetActivePlayer(isMyTurn);

            // Phase
            if (Enum.TryParse<GamePhase>(state.Phase, out var phase))
            {
                boardView.SetPhaseProgress((int)phase);
            }
        }

        /// <summary>
        /// Handle game over from either online or offline mode.
        /// </summary>
        private void HandleGameOverState(GameOver msg)
        {
            _gameActive = false;
            ApplyState(msg.FinalState);
            OnGameEnded?.Invoke(msg.WinnerIndex, msg.WinReason);
        }

        // ═════════════════════════════════════════════════
        //  QUERIES
        // ═════════════════════════════════════════════════

        /// <summary>Is it the local player's turn?</summary>
        public bool IsMyTurn => _currentState != null &&
                                _currentState.CurrentPlayer == _localPlayerIndex;

        /// <summary>The local player's seat index.</summary>
        public int LocalPlayerIndex => _localPlayerIndex;

        /// <summary>Current play mode.</summary>
        public PlayMode CurrentPlayMode => playMode;

        /// <summary>Whether a game is in progress.</summary>
        public bool IsGameActive => _gameActive;
    }
}
