// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Authoritative Host (Server-Side Game Runner)
//
//  "The server is the single source of truth. Clients
//   only render what the server confirms."
//
//  Wraps GameCore to run server-authoritative games.
//  Validates all actions, maintains game state, builds
//  per-player views (hidden info), records replay,
//  and broadcasts results. Pure C# — can run headless
//  on a dedicated server or locally for offline play.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace DualCraft.Networking
{
    using Battle;
    using Cards;
    using Core;

    /// <summary>
    /// An active game room running on the authoritative host.
    /// One AuthoritativeRoom per match. Lifetime: game start → game over + cleanup.
    /// </summary>
    public class AuthoritativeRoom
    {
        // ── Identity ────────────────────────────────────
        public string RoomId { get; }
        public RoomSettings Settings { get; }

        // ── Players ─────────────────────────────────────
        public PlayerSession[] Players { get; } = new PlayerSession[2];
        public int ConnectedCount => (Players[0]?.Connected == true ? 1 : 0) +
                                     (Players[1]?.Connected == true ? 1 : 0);

        // ── Game ────────────────────────────────────────
        public GameCore Core { get; private set; }
        public bool Started { get; private set; }
        public bool Finished { get; private set; }

        // ── Replay ──────────────────────────────────────
        private readonly ReplayRecorder _replay;
        private int _serverSequence;

        // ── RNG ─────────────────────────────────────────
        private readonly int _shuffleSeed;

        // ── Events (for the host to broadcast) ──────────
        public event Action<int, NetEnvelope> OnSendToPlayer;   // (playerIndex, envelope)
        public event Action<NetEnvelope> OnBroadcast;
        public event Action<AuthoritativeRoom> OnRoomClosed;

        // ── Disconnect timer ────────────────────────────
        private const int DisconnectTimeoutSec = 120;

        public AuthoritativeRoom(string roomId, RoomSettings settings)
        {
            RoomId = roomId;
            Settings = settings ?? new RoomSettings
            {
                TurnTimerSeconds = 90,
                GameMode = "standard",
            };
            _shuffleSeed = SecureRNG.NextInt();
            _replay = new ReplayRecorder(roomId);
        }

        // ═════════════════════════════════════════════════
        //  PLAYER MANAGEMENT
        // ═════════════════════════════════════════════════

        /// <summary>
        /// Add a player to the room. Returns their seat index (0 or 1), or -1 if full.
        /// </summary>
        public int AddPlayer(PlayerSession session)
        {
            for (int i = 0; i < 2; i++)
            {
                if (Players[i] == null)
                {
                    Players[i] = session;
                    session.SeatIndex = i;
                    return i;
                }
            }
            return -1; // room full
        }

        /// <summary>Mark a player as disconnected.</summary>
        public void PlayerDisconnected(int seatIndex)
        {
            if (seatIndex < 0 || seatIndex > 1 || Players[seatIndex] == null) return;
            Players[seatIndex].Connected = false;

            int other = 1 - seatIndex;
            if (Players[other]?.Connected == true)
            {
                SendToPlayer(other, new OpponentDisconnected
                {
                    Message = $"{Players[seatIndex].PlayerName} disconnected.",
                    TimeoutSeconds = DisconnectTimeoutSec,
                });
            }
        }

        /// <summary>Reconnect a player and send them a state snapshot.</summary>
        public void PlayerReconnected(int seatIndex)
        {
            if (seatIndex < 0 || seatIndex > 1 || Players[seatIndex] == null) return;
            Players[seatIndex].Connected = true;

            // Send full state snapshot to reconnecting player
            if (Started && !Finished)
            {
                SendToPlayer(seatIndex, new GameStateSnapshot
                {
                    RoomId = RoomId,
                    YourPlayerIndex = seatIndex,
                    State = BuildStateForPlayer(seatIndex),
                    ServerSequence = _serverSequence,
                });

                int other = 1 - seatIndex;
                if (Players[other]?.Connected == true)
                {
                    SendToPlayer(other, new OpponentReconnected
                    {
                        Message = $"{Players[seatIndex].PlayerName} reconnected.",
                    });
                }
            }
        }

        // ═════════════════════════════════════════════════
        //  GAME LIFECYCLE
        // ═════════════════════════════════════════════════

        /// <summary>
        /// Starts the game once both players are seated.
        /// The server initializes the engine with secure RNG.
        /// </summary>
        public void StartGame(CardDatabase cardDb, DeckData deck0, DeckData deck1)
        {
            if (Started) return;
            if (Players[0] == null || Players[1] == null) return;

            var engine = new BattleManager(cardDb);
            engine.InitGame(
                Players[0].PlayerName, deck0,
                Players[1].PlayerName, deck1
            );

            Core = new GameCore(engine);
            Core.OnGameOver += HandleGameOver;
            Started = true;

            _replay.Begin(
                Players[0].PlayerName, Players[1].PlayerName,
                Players[0].DeckId, Players[1].DeckId,
                _shuffleSeed
            );

            // Send initial state to each player (with hidden info filtering)
            for (int i = 0; i < 2; i++)
            {
                SendToPlayer(i, new GameStateSnapshot
                {
                    RoomId = RoomId,
                    YourPlayerIndex = i,
                    State = BuildStateForPlayer(i),
                    ServerSequence = _serverSequence,
                });
            }
        }

        /// <summary>
        /// Process an action from a player. This is THE authority.
        /// "Never trust the client. Validate everything."
        /// </summary>
        public void ProcessAction(int seatIndex, SerializableAction sa, int clientSeq)
        {
            if (!Started || Finished) return;
            if (seatIndex < 0 || seatIndex > 1) return;

            GameAction action = sa.ToGameAction();
            if (action == null)
            {
                SendToPlayer(seatIndex, new ActionRejected
                {
                    SequenceNum = clientSeq,
                    Reason = "Unknown action type.",
                });
                return;
            }

            // The GameCore validates turn ownership and phase
            ActionResult result = Core.Play(seatIndex, action);
            _serverSequence++;

            _replay.RecordMove(
                Core.State.TurnNumber, seatIndex, sa,
                result.Success, result.Reason
            );

            if (result.Success)
            {
                // Send confirmed state to both players
                for (int i = 0; i < 2; i++)
                {
                    SendToPlayer(i, new ActionConfirmed
                    {
                        SequenceNum = i == seatIndex ? clientSeq : -1,
                        Success = true,
                        Reason = "",
                        Action = sa,
                        State = BuildStateForPlayer(i),
                        LogMessage = Core.State.LastAction ?? "",
                    });
                }
            }
            else
            {
                SendToPlayer(seatIndex, new ActionRejected
                {
                    SequenceNum = clientSeq,
                    Reason = result.Reason,
                });
            }
        }

        // ═════════════════════════════════════════════════
        //  STATE SERIALIZATION
        // ═════════════════════════════════════════════════

        /// <summary>
        /// Build a serializable game state for a specific player.
        /// Hidden information (opponent's hand cards) is stripped.
        /// "The server selectively reveals information."
        /// </summary>
        private SerializableGameState BuildStateForPlayer(int viewerIndex)
        {
            var gs = Core.State;
            var sgs = new SerializableGameState
            {
                RoomId = RoomId,
                CurrentPlayer = gs.CurrentPlayer,
                Phase = gs.Phase.ToString(),
                TurnNumber = gs.TurnNumber,
                GameOver = gs.GameOver,
                Winner = gs.Winner ?? -1,
                ActiveDomainId = gs.ActiveDomain?.Card?.cardName ?? "",
                ActiveDomainOwner = gs.ActiveDomain?.Owner ?? -1,
                Players = new SerializablePlayerState[2],
                RecentLog = new List<SerializableLogEntry>(),
            };

            for (int i = 0; i < 2; i++)
            {
                var ps = gs.Players[i];
                var sps = new SerializablePlayerState
                {
                    Id = ps.Id,
                    Name = ps.Name,
                    ConjurorHp = ps.Conjuror.Hp,
                    ConjurorMaxHp = ps.Conjuror.MaxHp,
                    Will = ps.Will,
                    MaxWill = ps.MaxWill,
                    HandCount = ps.Hand.Count,
                    DeckCount = ps.Deck.Count,
                    AshePileCount = ps.AshePile.Count,
                    SealCount = ps.SealZone.Count,
                    // Only show hand cards to the owning player
                    HandCardIds = i == viewerIndex
                        ? BuildHandIds(ps)
                        : null,
                    Field = BuildField(ps),
                    Pillars = BuildPillars(ps),
                };
                sgs.Players[i] = sps;
            }

            // Last N log entries
            int logStart = Math.Max(0, gs.Log.Count - 10);
            for (int i = logStart; i < gs.Log.Count; i++)
            {
                sgs.RecentLog.Add(new SerializableLogEntry
                {
                    Turn = gs.TurnNumber,
                    Player = gs.CurrentPlayer,
                    Message = gs.Log[i].Message,
                    Type = gs.Log[i].Type.ToString(),
                });
            }

            return sgs;
        }

        private static string[] BuildHandIds(PlayerState ps)
        {
            var ids = new string[ps.Hand.Count];
            for (int i = 0; i < ps.Hand.Count; i++)
                ids[i] = ps.Hand[i].Card?.cardName ?? ps.Hand[i].InstanceId;
            return ids;
        }

        private static SerializableDaemon[] BuildField(PlayerState ps)
        {
            var arr = new SerializableDaemon[ps.Field.Count];
            for (int i = 0; i < ps.Field.Count; i++)
            {
                var d = ps.Field[i];
                var maskIds = new string[d.Masks.Count];
                for (int m = 0; m < d.Masks.Count; m++)
                    maskIds[m] = d.Masks[m].Card?.cardName ?? "";

                arr[i] = new SerializableDaemon
                {
                    InstanceId = d.InstanceId,
                    CardId = d.Card?.cardName ?? "",
                    CurrentAshe = d.CurrentAshe,
                    MaxAshe = d.MaxAshe,
                    Attack = d.Attack,
                    CanAttack = d.CanAttack,
                    HasAttacked = d.HasAttacked,
                    Frozen = d.Frozen,
                    Stealthed = d.Stealthed,
                    Entangled = d.Entangled,
                    HasTaunt = d.HasTaunt,
                    ShieldAmount = d.ShieldAmount,
                    ThornsDamage = d.ThornsDamage,
                    Silenced = d.Silenced,
                    MaskIds = maskIds,
                };
            }
            return arr;
        }

        private static SerializablePillar[] BuildPillars(PlayerState ps)
        {
            var arr = new SerializablePillar[ps.Pillars.Count];
            for (int i = 0; i < ps.Pillars.Count; i++)
            {
                var p = ps.Pillars[i];
                arr[i] = new SerializablePillar
                {
                    InstanceId = p.InstanceId,
                    CardId = p.Card?.cardName ?? "",
                    CurrentHp = p.CurrentHp,
                    MaxHp = p.MaxHp,
                    Loyalty = p.Loyalty,
                    Destroyed = p.Destroyed,
                    Revealed = p.Revealed,
                };
            }
            return arr;
        }

        // ═════════════════════════════════════════════════
        //  GAME OVER
        // ═════════════════════════════════════════════════

        private void HandleGameOver(int winner, string reason)
        {
            Finished = true;
            ReplayData replay = _replay.Finish(winner, reason);

            for (int i = 0; i < 2; i++)
            {
                SendToPlayer(i, new GameOver
                {
                    WinnerIndex = winner,
                    WinReason = reason,
                    FinalState = BuildStateForPlayer(i),
                    Replay = replay,
                });
            }

            OnRoomClosed?.Invoke(this);
        }

        // ═════════════════════════════════════════════════
        //  TRANSPORT HELPERS
        // ═════════════════════════════════════════════════

        private void SendToPlayer<T>(int seatIndex, T msg) where T : class
        {
            var envelope = NetEnvelope.Create(msg, "server");
            OnSendToPlayer?.Invoke(seatIndex, envelope);
        }

        private void Broadcast<T>(T msg) where T : class
        {
            var envelope = NetEnvelope.Create(msg, "server");
            OnBroadcast?.Invoke(envelope);
        }
    }

    /// <summary>
    /// Tracks a player's connection session within a room.
    /// </summary>
    public class PlayerSession
    {
        public string PlayerId;
        public string PlayerName;
        public string DeckId;
        public string AuthToken;
        public int SeatIndex;
        public bool Connected = true;
    }
}
