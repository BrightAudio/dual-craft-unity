// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Relay Game Client
//
//  Runs on the JOINING player's machine. Sends actions
//  to the host via Unity Relay and receives authoritative
//  state updates back. From this player's perspective,
//  the host acts as the "server".
// ═══════════════════════════════════════════════════════

using System;
using System.Text;
using UnityEngine;
using Unity.Services.Authentication;

namespace DualCraft.Networking
{
    using Battle;

    /// <summary>
    /// Relay-based game client for the joining player.
    /// Connects via join code, sends actions, receives state.
    /// </summary>
    public class RelayGameClient : MonoBehaviour
    {
        private RelayManager _relay;
        private string _playerName;
        private string _playerId;
        private string _deckId;
        private int _actionSequence;
        private bool _gameActive;

        // Events for the UI
        public event Action OnConnectedToHost;
        public event Action<GameStateSnapshot> OnGameStateReceived;
        public event Action<ActionConfirmed> OnActionConfirmed;
        public event Action<ActionRejected> OnActionRejected;
        public event Action<GameOver> OnGameOverReceived;
        public event Action<OpponentDisconnected> OnHostDisconnected;
        public event Action<string> OnError;

        public bool IsConnected => _relay != null && _relay.IsConnected;
        public int ActionSequence => _actionSequence;

        /// <summary>
        /// Initialize and connect to a host via join code.
        /// </summary>
        public async void Connect(string joinCode, string playerName, string deckId)
        {
            _relay = RelayManager.Instance;
            _playerName = playerName;
            _playerId = AuthenticationService.Instance.PlayerId;
            _deckId = deckId;
            _actionSequence = 0;

            _relay.OnClientConnected += HandleConnected;
            _relay.OnDataReceived += HandleIncomingData;
            _relay.OnClientDisconnected += HandleDisconnected;

            bool success = await _relay.JoinGame(joinCode);
            if (!success)
            {
                OnError?.Invoke("Failed to connect to host.");
            }
        }

        private void OnDestroy()
        {
            if (_relay != null)
            {
                _relay.OnClientConnected -= HandleConnected;
                _relay.OnDataReceived -= HandleIncomingData;
                _relay.OnClientDisconnected -= HandleDisconnected;
            }
        }

        // ═════════════════════════════════════════════════
        //  CONNECTION
        // ═════════════════════════════════════════════════

        private void HandleConnected()
        {
            Debug.Log("[RelayGameClient] Connected to host. Sending join request...");

            // Tell the host who we are and what deck we're using
            _relay.SendMessage(new JoinRoomRequest
            {
                PlayerId = _playerId,
                PlayerName = _playerName,
                RoomId = _relay.JoinCode,
                DeckId = _deckId,
                AuthToken = "",
            }, _playerId);

            _gameActive = true;
            OnConnectedToHost?.Invoke();
        }

        private void HandleDisconnected()
        {
            _gameActive = false;
            OnHostDisconnected?.Invoke(new OpponentDisconnected
            {
                Message = "Lost connection to host.",
                TimeoutSeconds = 30,
            });
        }

        // ═════════════════════════════════════════════════
        //  RECEIVE FROM HOST
        // ═════════════════════════════════════════════════

        private void HandleIncomingData(byte[] data)
        {
            string json = Encoding.UTF8.GetString(data);
            NetEnvelope envelope;
            try { envelope = JsonUtility.FromJson<NetEnvelope>(json); }
            catch { Debug.LogWarning("[RelayGameClient] Bad envelope from host."); return; }

            switch (envelope.Type)
            {
                case nameof(GameStateSnapshot):
                    var snap = JsonUtility.FromJson<GameStateSnapshot>(envelope.Payload);
                    OnGameStateReceived?.Invoke(snap);
                    break;

                case nameof(ActionConfirmed):
                    var confirmed = JsonUtility.FromJson<ActionConfirmed>(envelope.Payload);
                    OnActionConfirmed?.Invoke(confirmed);
                    break;

                case nameof(ActionRejected):
                    var rejected = JsonUtility.FromJson<ActionRejected>(envelope.Payload);
                    OnActionRejected?.Invoke(rejected);
                    break;

                case nameof(GameOver):
                    var over = JsonUtility.FromJson<GameOver>(envelope.Payload);
                    _gameActive = false;
                    OnGameOverReceived?.Invoke(over);
                    break;

                case nameof(RoomJoined):
                    // Confirmation we're in the room
                    break;

                case nameof(OpponentDisconnected):
                    var disc = JsonUtility.FromJson<OpponentDisconnected>(envelope.Payload);
                    OnHostDisconnected?.Invoke(disc);
                    break;

                case nameof(PongMessage):
                    // Latency tracking
                    break;

                default:
                    Debug.LogWarning($"[RelayGameClient] Unknown: {envelope.Type}");
                    break;
            }
        }

        // ═════════════════════════════════════════════════
        //  SEND ACTIONS TO HOST
        // ═════════════════════════════════════════════════

        /// <summary>Submit a game action to the host for validation.</summary>
        public void SubmitAction(GameAction action)
        {
            if (!_gameActive || !IsConnected) return;

            _actionSequence++;
            _relay.SendMessage(new ActionRequest
            {
                PlayerId = _playerId,
                PlayerIndex = 1, // guest is always seat 1
                SequenceNum = _actionSequence,
                Action = SerializableAction.FromGameAction(action),
            }, _playerId);
        }

        /// <summary>Send a leave notification.</summary>
        public void Leave()
        {
            if (IsConnected)
            {
                _relay.SendMessage(new LeaveRequest
                {
                    PlayerId = _playerId,
                    RoomId = _relay.JoinCode,
                }, _playerId);
            }
            _gameActive = false;
            _relay?.Shutdown();
        }
    }
}
