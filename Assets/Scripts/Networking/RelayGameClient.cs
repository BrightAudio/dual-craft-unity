// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Relay Game Client
//
//  Runs on the JOINING player's machine. Sends actions
//  to the host via Unity Relay and receives authoritative
//  state updates back. From this player's perspective,
//  the host acts as the "server".
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
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
        private Cards.DeckData _deckData;
        private int _actionSequence;
        private bool _gameActive;
        private bool _joinAccepted;
        private float _lastJoinRequestSentAt = -999f;
        private int _joinRequestAttempts;
        private int _lastReceivedServerSequence = -1;
        private int _lastAppliedServerSequence = -1;
        private float _lastPrivateStateRequestAt = -999f;

        public GameStateSnapshot LatestSnapshot { get; private set; }
        public int LastAppliedServerSequence => _lastAppliedServerSequence;

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
        public async void Connect(string joinCode, string playerName, string deckId, Cards.DeckData deckData = null)
        {
            _relay = RelayManager.Instance;
            _playerName = playerName;
            _playerId = ResolvePlayerId();
            _deckId = deckId;
            _deckData = deckData;
            _actionSequence = 0;
            _joinAccepted = false;
            _lastJoinRequestSentAt = -999f;
            _joinRequestAttempts = 0;
            _lastReceivedServerSequence = -1;
            _lastAppliedServerSequence = -1;
            _lastPrivateStateRequestAt = -999f;

            if (_relay != null)
            {
                _relay.OnClientConnected -= HandleConnected;
                _relay.OnDataReceived -= HandleIncomingData;
                _relay.OnClientDisconnected -= HandleDisconnected;
                _relay.OnClientConnected += HandleConnected;
                _relay.OnDataReceived += HandleIncomingData;
                _relay.OnClientDisconnected += HandleDisconnected;
            }

            bool success = await _relay.JoinGame(joinCode);
            if (!success)
            {
                OnError?.Invoke(string.IsNullOrWhiteSpace(_relay.LastError)
                    ? "Failed to connect to host."
                    : _relay.LastError);
            }
        }

        private static string ResolvePlayerId()
        {
            try
            {
                var auth = AuthenticationService.Instance;
                if (auth != null && auth.IsSignedIn && !string.IsNullOrWhiteSpace(auth.PlayerId))
                    return auth.PlayerId;
            }
            catch
            {
                // Keep the join flow from crashing if anonymous auth is unavailable.
            }

            return $"local-{SystemInfo.deviceUniqueIdentifier}";
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

        private void Update()
        {
            if (!_gameActive || _joinAccepted || !IsConnected)
                return;

            if (_joinRequestAttempts >= 10)
                return;

            if (Time.unscaledTime - _lastJoinRequestSentAt < 1f)
                return;

            SendJoinRequest();
        }

        // ═════════════════════════════════════════════════
        //  CONNECTION
        // ═════════════════════════════════════════════════

        private void HandleConnected()
        {
            Debug.Log("[RelayGameClient] Connected to host. Sending join request...");
            _gameActive = true;
            SendJoinRequest();
            OnConnectedToHost?.Invoke();
        }

        private void SendJoinRequest()
        {
            // Tell the host who we are and send our full deck
            var joinReq = new JoinRoomRequest
            {
                PlayerId = _playerId,
                PlayerName = _playerName,
                RoomId = _relay.JoinCode,
                DeckId = _deckId,
                AuthToken = "",
            };

            if (_deckData != null)
            {
                joinReq.MainDeck = BuildDeckEntries(_deckData.cards);
                joinReq.PillarDeck = BuildDeckEntries(_deckData.pillars);
                joinReq.WardIds = _deckData.wardIds;
            }

            _relay.SendMessage(joinReq, _playerId);
            _lastJoinRequestSentAt = Time.unscaledTime;
            _joinRequestAttempts++;
            Debug.Log($"[RelayGameClient] Join request sent ({_joinRequestAttempts}).");
        }

        private static SerializableDeckEntry[] BuildDeckEntries(Cards.DeckEntry[] entries)
        {
            if (entries == null)
                return Array.Empty<SerializableDeckEntry>();

            return entries
                .Where(entry => entry.card != null && !string.IsNullOrWhiteSpace(entry.card.cardId) && entry.count > 0)
                .GroupBy(entry => entry.card.cardId)
                .Select(group => new SerializableDeckEntry
                {
                    CardId = group.Key,
                    Count = group.Sum(entry => entry.count),
                })
                .ToArray();
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
                    if (snap == null)
                        break;
                    if (!ValidatePrivateState(snap.State, snap.YourPlayerIndex, snap.ServerSequence, "snapshot", out int snapshotSeat))
                        break;
                    snap.YourPlayerIndex = snapshotSeat;
                    if (HandleDuplicateState(snap.ServerSequence, "Snapshot"))
                        break;
                    LatestSnapshot = snap;
                    _joinAccepted = true;
                    _lastReceivedServerSequence = snap.ServerSequence;
                    AcknowledgeReceivedState(snap.ServerSequence, "ReceivedSnapshot");
                    OnGameStateReceived?.Invoke(snap);
                    break;

                case nameof(ActionConfirmed):
                    var confirmed = JsonUtility.FromJson<ActionConfirmed>(envelope.Payload);
                    if (confirmed == null)
                        break;
                    if (!ValidatePrivateState(confirmed.State, 1, confirmed.ServerSequence, "action confirmation", out _))
                        break;
                    if (HandleDuplicateState(confirmed.ServerSequence, "ActionConfirmed"))
                        break;
                    if (confirmed.State != null)
                    {
                        _joinAccepted = true;
                        _lastReceivedServerSequence = confirmed.ServerSequence;
                        LatestSnapshot = new GameStateSnapshot
                    {
                        RoomId = confirmed.State.RoomId,
                        YourPlayerIndex = 1,
                        State = confirmed.State,
                        ServerSequence = confirmed.ServerSequence,
                    };
                    }
                    AcknowledgeReceivedState(confirmed.ServerSequence, "ReceivedActionConfirmed");
                    OnActionConfirmed?.Invoke(confirmed);
                    break;

                case nameof(ActionRejected):
                    var rejected = JsonUtility.FromJson<ActionRejected>(envelope.Payload);
                    OnActionRejected?.Invoke(rejected);
                    break;

                case nameof(GameOver):
                    var over = JsonUtility.FromJson<GameOver>(envelope.Payload);
                    if (over == null)
                        break;
                    if (!ValidatePrivateState(over.FinalState, 1, over.ServerSequence, "game over", out _))
                        break;
                    if (HandleDuplicateState(over.ServerSequence, "GameOver"))
                        break;
                    if (over.FinalState != null)
                    {
                        _joinAccepted = true;
                        _lastReceivedServerSequence = over.ServerSequence;
                        LatestSnapshot = new GameStateSnapshot
                    {
                        RoomId = over.FinalState.RoomId,
                        YourPlayerIndex = 1,
                        State = over.FinalState,
                        ServerSequence = over.ServerSequence,
                    };
                    }
                    AcknowledgeReceivedState(over.ServerSequence, "ReceivedGameOver");
                    OnGameOverReceived?.Invoke(over);
                    _gameActive = false;
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

        private bool ValidatePrivateState(SerializableGameState state, int expectedSeat, int serverSequence,
            string stateKind, out int resolvedSeat)
        {
            if (NetworkStateProjector.TryResolvePrivateHand(state, expectedSeat, out resolvedSeat, out string error))
                return true;

            string message = $"Joined-player card data missing from {stateKind}: {error}. Requesting resync.";
            Debug.LogError($"[RelayGameClient] {message}");
            OnError?.Invoke(message);
            RequestPrivateState(serverSequence, message);
            return false;
        }

        private void RequestPrivateState(int serverSequence, string reason)
        {
            if (!IsConnected || Time.unscaledTime - _lastPrivateStateRequestAt < 0.75f)
                return;

            _lastPrivateStateRequestAt = Time.unscaledTime;
            _relay.SendMessage(new PrivateStateRequest
            {
                PlayerId = _playerId,
                RoomId = _relay.JoinCode,
                PlayerIndex = 1,
                LastServerSequence = serverSequence,
                Reason = reason,
            }, _playerId);
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

        /// <summary>
        /// Called by the battle scene after a server state has actually been applied to the UI.
        /// </summary>
        public void AcknowledgeAppliedState(int serverSequence, string stateKind)
        {
            if (serverSequence < _lastAppliedServerSequence)
                return;

            _lastAppliedServerSequence = serverSequence;
            SendStateAck(serverSequence, stateKind ?? "AppliedState", "Applied");
        }

        private void AcknowledgeReceivedState(int serverSequence, string stateKind)
        {
            SendStateAck(serverSequence, stateKind ?? "ReceivedState", "Received");
        }

        private bool HandleDuplicateState(int serverSequence, string stateKind)
        {
            if (serverSequence <= _lastAppliedServerSequence)
            {
                SendStateAck(serverSequence, stateKind, "Reconfirmed");
                return true;
            }

            if (serverSequence <= _lastReceivedServerSequence)
            {
                SendStateAck(serverSequence, $"Received{stateKind}", "Reconfirmed received");
                return true;
            }

            return false;
        }

        private void SendStateAck(int serverSequence, string stateKind, string logVerb)
        {
            if (!_gameActive || !IsConnected || serverSequence < 0)
                return;

            _relay.SendMessage(new StateAppliedAck
            {
                PlayerId = _playerId,
                RoomId = _relay.JoinCode,
                PlayerIndex = 1,
                ServerSequence = serverSequence,
                StateKind = stateKind ?? "State",
            }, _playerId);

            Debug.Log($"[RelayGameClient] {logVerb} {stateKind ?? "State"} seq {serverSequence}; ACK sent.");
        }

        /// <summary>Concede the active match and wait for the host's authoritative GameOver.</summary>
        public void Forfeit()
        {
            if (!_gameActive || !IsConnected) return;

            _relay.SendMessage(new ForfeitRequest
            {
                PlayerId = _playerId,
                RoomId = _relay.JoinCode,
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
