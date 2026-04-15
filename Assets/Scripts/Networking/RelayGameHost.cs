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
using System.Text;
using UnityEngine;

namespace DualCraft.Networking
{
    using Battle;
    using Cards;
    using Core;

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

        /// <summary>The room being hosted.</summary>
        public AuthoritativeRoom Room => _room;

        // Events for the local host UI
        public event Action OnGuestJoined;
        public event Action OnGameStarted;
        public event Action<int, string> OnGameEnded;

        /// <summary>
        /// Initialize the host with game data.
        /// Call this after RelayManager.StartHost() succeeds.
        /// </summary>
        public void Initialize(CardDatabase cardDb, DeckData hostDeck,
                               string hostPlayerName, string hostPlayerId)
        {
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

        private void OnDestroy()
        {
            if (_relay != null)
            {
                _relay.OnClientConnected -= HandleGuestConnected;
                _relay.OnDataReceived -= HandleIncomingData;
                _relay.OnClientDisconnected -= HandleGuestDisconnected;
            }
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

                case nameof(PingMessage):
                    HandlePing(envelope);
                    break;

                default:
                    Debug.LogWarning($"[RelayGameHost] Unknown message: {envelope.Type}");
                    break;
            }
        }

        private void HandleJoinRequest(NetEnvelope envelope)
        {
            var req = JsonUtility.FromJson<JoinRoomRequest>(envelope.Payload);

            // Add guest as seat 1
            _room.AddPlayer(new PlayerSession
            {
                PlayerId = req.PlayerId,
                PlayerName = req.PlayerName,
                DeckId = req.DeckId,
            });

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
            if (req.MainDeck == null || req.MainDeck.Length == 0)
                return null;

            var deck = ScriptableObject.CreateInstance<DeckData>();
            deck.deckName = $"{req.PlayerName}'s Deck";

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

            return deck;
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

        private void HandlePing(NetEnvelope envelope)
        {
            var ping = JsonUtility.FromJson<PingMessage>(envelope.Payload);
            _relay.SendMessage(new PongMessage
            {
                ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ClientTime = ping.ClientTime,
            }, "host");
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
                string json = JsonUtility.ToJson(envelope);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                _relay.SendData(bytes);
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

        // ═════════════════════════════════════════════════
        //  LOCAL MESSAGE PROCESSING
        // ═════════════════════════════════════════════════

        /// <summary>Process a message intended for the local host player.</summary>
        public event Action<NetEnvelope> OnLocalMessage;

        private void ProcessLocalMessage(NetEnvelope envelope)
        {
            OnLocalMessage?.Invoke(envelope);
        }
    }
}
