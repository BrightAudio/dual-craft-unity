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
using System.Linq;

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

        /// <summary>Send the current authoritative state to one player without changing connection status.</summary>
        public void SendStateSnapshot(int seatIndex)
        {
            if (!Started || Finished) return;
            if (seatIndex < 0 || seatIndex > 1 || Players[seatIndex] == null) return;

            SendToPlayer(seatIndex, new GameStateSnapshot
            {
                RoomId = RoomId,
                YourPlayerIndex = seatIndex,
                State = BuildStateForPlayer(seatIndex),
                ServerSequence = _serverSequence,
            });
        }

        /// <summary>
        /// Sends a new ordered snapshot when a client received the initial state before
        /// its battle scene was ready to render private card data.
        /// </summary>
        public void SendFreshStateSnapshot(int seatIndex)
        {
            if (!Started || Finished) return;
            if (seatIndex < 0 || seatIndex > 1 || Players[seatIndex] == null) return;

            _serverSequence++;
            SendToPlayer(seatIndex, new GameStateSnapshot
            {
                RoomId = RoomId,
                YourPlayerIndex = seatIndex,
                State = BuildStateForPlayer(seatIndex),
                ServerSequence = _serverSequence,
            });
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
            Core.Engine.CompleteSetup();

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
            UnityEngine.Debug.Log($"[Authority] seq={_serverSequence} seat={seatIndex} "
                + $"action={sa.ActionType ?? "unknown"} success={result.Success} "
                + $"reason={result.Reason ?? ""}");

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
                        ServerSequence = _serverSequence,
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

        /// <summary>Resolve a voluntary concession as an authoritative game over.</summary>
        public void ForfeitPlayer(int seatIndex)
        {
            if (!Started || Finished) return;
            if (seatIndex < 0 || seatIndex > 1) return;

            int winner = 1 - seatIndex;
            string playerName = Players[seatIndex]?.PlayerName ?? $"Player {seatIndex + 1}";
            _serverSequence++;
            var state = Core.Engine.State;
            state.Winner = winner;
            state.GameOver = true;
            string reason = $"{playerName} forfeited.";
            state.LastAction = reason;
            state.Log.Add(new LogEntry
            {
                Turn = state.TurnNumber,
                Player = seatIndex,
                Message = reason,
                Type = LogEntryType.System,
            });

            HandleGameOver(winner, reason);
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
                ProtocolVersion = SerializableGameState.CurrentProtocolVersion,
                RoomId = RoomId,
                CurrentPlayer = gs.CurrentPlayer,
                Phase = gs.Phase.ToString(),
                TurnNumber = gs.TurnNumber,
                GameOver = gs.GameOver,
                Winner = gs.Winner ?? -1,
                ActiveDomainId = gs.ActiveDomain?.Card?.cardId ?? "",
                ActiveDomainOwner = gs.ActiveDomain?.Owner ?? -1,
                Players = new SerializablePlayerState[2],
                HasViewerPrivateState = true,
                ViewerPlayerIndex = viewerIndex,
                ViewerHandCardIds = BuildHandIds(gs.Players[viewerIndex]),
                ViewerHandCards = BuildHandSpecs(gs.Players[viewerIndex]),
                RecentLog = new List<SerializableLogEntry>(),
            };

            for (int i = 0; i < 2; i++)
            {
                var ps = gs.Players[i];
                var sps = new SerializablePlayerState
                {
                    Id = ps.Id,
                    Name = ps.Name,
                    InvokerHp = ps.Invoker.Hp,
                    InvokerMaxHp = ps.Invoker.MaxHp,
                    InvokerCardId = ps.InvokerCard?.cardId ?? "",
                    InvokerArchetype = ps.InvokerArchetype.ToString(),
                    Will = ps.Will,
                    MaxWill = ps.MaxWill,
                    HandCount = ps.Hand.Count,
                    DeckCount = ps.Deck.Count,
                    AshePileCount = ps.AshePile.Count,
                    SealCount = ps.SealZone.Count,
                    WardCount = ps.Wards?.Count(w => w != null && !string.IsNullOrWhiteSpace(w.WardId)) ?? 0,
                    // Only show hand cards to the owning player
                    HandCardIds = i == viewerIndex
                        ? BuildHandIds(ps)
                        : null,
                    HandCards = i == viewerIndex
                        ? BuildHandSpecs(ps)
                        : null,
                    Field = BuildField(ps),
                    Pillars = BuildPillars(ps),
                    AsheCards = BuildAsheCards(ps),
                    SourcePlayedThisTurn = ps.SourcePlayedThisTurn,
                    SourceRaidUsedThisTurn = ps.SourceRaidUsedThisTurn,
                };
                sgs.Players[i] = sps;
            }

            // Last N log entries
            int logStart = Math.Max(0, gs.Log.Count - 10);
            for (int i = logStart; i < gs.Log.Count; i++)
            {
                var entry = gs.Log[i];
                sgs.RecentLog.Add(new SerializableLogEntry
                {
                    Turn = entry.Turn,
                    Player = entry.Player,
                    Message = entry.Message,
                    Type = entry.Type.ToString(),
                });
            }

            return sgs;
        }

        private static string[] BuildHandIds(PlayerState ps)
        {
            var ids = new string[ps.Hand.Count];
            for (int i = 0; i < ps.Hand.Count; i++)
                ids[i] = ps.Hand[i].Card?.cardId ?? ps.Hand[i].InstanceId;
            return ids;
        }

        private static SerializableCardSpec[] BuildHandSpecs(PlayerState ps)
        {
            var specs = new SerializableCardSpec[ps.Hand.Count];
            for (int i = 0; i < ps.Hand.Count; i++)
                specs[i] = BuildCardSpec(ps.Hand[i].Card);
            return specs;
        }

        private static SerializableDaemon[] BuildField(PlayerState ps)
        {
            var arr = new SerializableDaemon[ps.Field.Count];
            for (int i = 0; i < ps.Field.Count; i++)
            {
                var d = ps.Field[i];
                var maskIds = new string[d.Masks.Count];
                for (int m = 0; m < d.Masks.Count; m++)
                    maskIds[m] = d.Masks[m].Card?.cardId ?? "";

                arr[i] = new SerializableDaemon
                {
                    InstanceId = d.InstanceId,
                    CardId = d.Card?.cardId ?? "",
                    Card = BuildCardSpec(d.Card),
                    CurrentAshe = d.CurrentAshe,
                    MaxAshe = d.MaxAshe,
                    Attack = d.Attack,
                    AsheCost = d.AsheCost,
                    LaneIndex = d.LaneIndex,
                    CanAttack = d.CanAttack,
                    HasAttacked = d.HasAttacked,
                    Frozen = d.Frozen,
                    Stealthed = d.Stealthed,
                    Entangled = d.Entangled,
                    HasTaunt = d.HasTaunt,
                    ShieldAmount = d.ShieldAmount,
                    ThornsDamage = d.ThornsDamage,
                    Silenced = d.Silenced,
                    SilencedTurns = d.SilencedTurns,
                    Marked = d.Marked,
                    MarkedBonusDamage = d.MarkedBonusDamage,
                    MarkedTurns = d.MarkedTurns,
                    Fractured = d.Fractured,
                    FracturedTurns = d.FracturedTurns,
                    Haunted = d.Haunted,
                    HauntedLifeLoss = d.HauntedLifeLoss,
                    HauntedTurns = d.HauntedTurns,
                    Corrupted = d.Corrupted,
                    CorruptedTurns = d.CorruptedTurns,
                    Overloaded = d.Overloaded,
                    OverloadAttackBonus = d.OverloadAttackBonus,
                    OverloadBacklash = d.OverloadBacklash,
                    OverloadedTurns = d.OverloadedTurns,
                    Taxed = d.Taxed,
                    TaxedExtraCost = d.TaxedExtraCost,
                    TaxedTurns = d.TaxedTurns,
                    Sundered = d.Sundered,
                    SunderedTurns = d.SunderedTurns,
                    IsSecondFormBound = d.IsSecondFormBound,
                    BoundAnchorInstanceIds = d.BoundAnchorInstanceIds?.ToArray(),
                    IsBindAnchor = d.IsBindAnchor,
                    BoundSecondFormInstanceId = d.BoundSecondFormInstanceId,
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
                    CardId = p.Card?.cardId ?? "",
                    Card = BuildCardSpec(p.Card),
                    CurrentHp = p.CurrentHp,
                    MaxHp = p.MaxHp,
                    Loyalty = p.Loyalty,
                    Destroyed = p.Destroyed,
                    Revealed = p.Revealed,
                };
            }
            return arr;
        }

        private static SerializableAsheCard[] BuildAsheCards(PlayerState ps)
        {
            var arr = new SerializableAsheCard[ps.AsheCards.Count];
            for (int i = 0; i < ps.AsheCards.Count; i++)
            {
                var a = ps.AsheCards[i];
                arr[i] = new SerializableAsheCard
                {
                    InstanceId = a.InstanceId,
                    CardId = a.Card?.cardId ?? "",
                    Card = BuildCardSpec(a.Card),
                    AssignedDaemonInstanceId = a.AssignedDaemonInstanceId,
                    ShieldRemaining = a.ShieldRemaining,
                    BuffTurnsRemaining = a.BuffTurnsRemaining,
                    SuppressedTurnsRemaining = a.SuppressedTurnsRemaining,
                };
            }
            return arr;
        }

        private static SerializableCardSpec BuildCardSpec(CardData card)
        {
            if (card == null)
                return null;

            var spec = new SerializableCardSpec
            {
                CardId = card.cardId,
                Name = card.cardName,
                Category = card.category.ToString(),
                Rarity = card.rarity.ToString(),
                Description = card.description,
                FlavorText = card.flavorText,
                Cost = card.GetWillCost(),
            };

            switch (card)
            {
                case DaemonCardData daemon:
                    spec.Element = daemon.element.ToString();
                    spec.CreatureType = daemon.creatureType.ToString();
                    spec.Attack = daemon.attack;
                    spec.Life = daemon.ashe;
                    spec.AttackCost = daemon.asheCost;
                    spec.Ranged = daemon.rangedAttack;
                    spec.AttackPattern = daemon.attackPattern.ToString();
                    break;
                case AsheCardData source:
                    spec.Element = source.matchType == AsheMatchType.Element ? source.targetElement.ToString() : "";
                    spec.CreatureType = source.GetAffinityType().ToString();
                    spec.SourceSePerTurn = source.sePerTurn;
                    break;
                case HexCardData hex:
                    spec.Element = hex.effectElement.ToString();
                    break;
                case DispelCardData dispel:
                    spec.Element = dispel.responseElement.ToString();
                    spec.CreatureType = dispel.responseCreatureType.ToString();
                    spec.CanCounterAttack = dispel.canCounterAttack;
                    spec.CanCounterHex = dispel.canCounterHex;
                    spec.CanCounterDomain = dispel.canCounterDomain;
                    break;
                case DomainCardData domain:
                    spec.Element = domain.effectElement.ToString();
                    break;
            }

            return spec;
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
                    ServerSequence = _serverSequence,
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
