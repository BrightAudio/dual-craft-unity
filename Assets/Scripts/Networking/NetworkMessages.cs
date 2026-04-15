// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Network Messages (Serializable Protocol)
//
//  "Shared protocols — WebSockets over JSON ensure
//   consistent communication across platforms."
//
//  Every message between client↔server is one of these
//  types. All are [Serializable] POCOs — JSON-ready,
//  zero Unity dependencies. This IS the wire protocol.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace DualCraft.Networking
{
    using Battle;
    using Core;

    // ─── Envelope ────────────────────────────────────────

    /// <summary>
    /// Every network message is wrapped in this envelope.
    /// The Type field determines how to deserialize Payload.
    /// </summary>
    [Serializable]
    public class NetEnvelope
    {
        public string Type;
        public string Payload;   // JSON of the inner message
        public long Timestamp;   // Unix ms, set by sender
        public string SenderId;  // player or server id

        public static NetEnvelope Create<T>(T msg, string senderId) where T : class
        {
            return new NetEnvelope
            {
                Type = typeof(T).Name,
                Payload = JsonUtility.ToJson(msg),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SenderId = senderId,
            };
        }
    }

    // We need a minimal JSON helper that works without Unity
    // when used in a standalone server context. For the Unity
    // client, UnityEngine.JsonUtility is fine. For portability,
    // provide a shim interface.
    public static class JsonUtility
    {
        // In Unity builds, this delegates to UnityEngine.JsonUtility.
        // In standalone server builds, swap to System.Text.Json or Newtonsoft.
        public static string ToJson(object obj)
        {
#if UNITY_5_3_OR_NEWER
            return UnityEngine.JsonUtility.ToJson(obj);
#else
            return System.Text.Json.JsonSerializer.Serialize(obj);
#endif
        }

        public static T FromJson<T>(string json)
        {
#if UNITY_5_3_OR_NEWER
            return UnityEngine.JsonUtility.FromJson<T>(json);
#else
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
#endif
        }
    }

    // ─── Client → Server Messages ────────────────────────

    /// <summary>Player wants to join a room.</summary>
    [Serializable]
    public class JoinRoomRequest
    {
        public string PlayerId;
        public string PlayerName;
        public string RoomId;       // empty = matchmaking
        public string DeckId;       // which deck to use
        public string AuthToken;    // JWT or session token
    }

    /// <summary>Player wants to create a private room.</summary>
    [Serializable]
    public class CreateRoomRequest
    {
        public string PlayerId;
        public string PlayerName;
        public string DeckId;
        public string AuthToken;
        public RoomSettings Settings;
    }

    /// <summary>Player submits a game action.</summary>
    [Serializable]
    public class ActionRequest
    {
        public string PlayerId;
        public int PlayerIndex;
        public int SequenceNum;     // client-side counter for ordering
        public SerializableAction Action;
    }

    /// <summary>Player requests a rematch.</summary>
    [Serializable]
    public class RematchRequest
    {
        public string PlayerId;
        public string RoomId;
    }

    /// <summary>Player leaves a game.</summary>
    [Serializable]
    public class LeaveRequest
    {
        public string PlayerId;
        public string RoomId;
    }

    /// <summary>Heartbeat/keepalive ping.</summary>
    [Serializable]
    public class PingMessage
    {
        public long ClientTime;
    }

    // ─── Server → Client Messages ────────────────────────

    /// <summary>Room was joined or created successfully.</summary>
    [Serializable]
    public class RoomJoined
    {
        public string RoomId;
        public int PlayerIndex;     // 0 or 1
        public string OpponentName;
        public RoomSettings Settings;
        public bool GameStarted;    // true if both players present
    }

    /// <summary>Waiting for an opponent.</summary>
    [Serializable]
    public class WaitingForOpponent
    {
        public string RoomId;
        public string Message;
    }

    /// <summary>
    /// Full game state snapshot — sent at game start and
    /// on reconnect. This is the authoritative truth.
    /// "The server is the single source of truth."
    /// </summary>
    [Serializable]
    public class GameStateSnapshot
    {
        public string RoomId;
        public int YourPlayerIndex;
        public SerializableGameState State;
        public int ServerSequence;  // server's action counter
    }

    /// <summary>
    /// Incremental state update after an action.
    /// Clients apply this to their local state.
    /// </summary>
    [Serializable]
    public class ActionConfirmed
    {
        public int SequenceNum;
        public bool Success;
        public string Reason;       // empty if success
        public SerializableAction Action;
        public SerializableGameState State; // full state after action
        public string LogMessage;
    }

    /// <summary>Action was rejected by the server.</summary>
    [Serializable]
    public class ActionRejected
    {
        public int SequenceNum;
        public string Reason;
    }

    /// <summary>Game over notification.</summary>
    [Serializable]
    public class GameOver
    {
        public int WinnerIndex;
        public string WinReason;
        public SerializableGameState FinalState;
        public ReplayData Replay;
    }

    /// <summary>Opponent disconnected.</summary>
    [Serializable]
    public class OpponentDisconnected
    {
        public string Message;
        public int TimeoutSeconds;  // time before auto-win
    }

    /// <summary>Opponent reconnected.</summary>
    [Serializable]
    public class OpponentReconnected
    {
        public string Message;
    }

    /// <summary>Server error.</summary>
    [Serializable]
    public class ServerError
    {
        public string Code;
        public string Message;
    }

    /// <summary>Heartbeat response with latency data.</summary>
    [Serializable]
    public class PongMessage
    {
        public long ServerTime;
        public long ClientTime;     // echoed back
    }

    // ─── Serializable Data Types ─────────────────────────

    /// <summary>
    /// Room configuration. Sent at creation, shared with both players.
    /// </summary>
    [Serializable]
    public class RoomSettings
    {
        public int TurnTimerSeconds;    // 0 = no timer
        public bool AllowSpectators;
        public bool RankedMatch;
        public string GameMode;         // "standard", "draft", etc.
    }

    /// <summary>
    /// Action serialized for the wire. Maps to GameAction types
    /// but uses simple fields instead of polymorphism.
    /// </summary>
    [Serializable]
    public class SerializableAction
    {
        public string ActionType;   // "PlayDaemon", "Attack", etc.
        public int HandIndex;
        public int FieldIndex;
        public int TargetIndex;
        public int PillarIndex;
        public int AbilityIndex;
        public int ConsumeIndex;
        public string TargetType;   // "Daemon", "Pillar", "Conjuror"
        public string DispelTarget; // "Domain", "Mask", "Seal", "Any"

        /// <summary>Convert to a GameAction for the engine.</summary>
        public GameAction ToGameAction()
        {
            return ActionType switch
            {
                "DrawCard" => new DrawCardAction(),
                "PlayDaemon" => new PlayDaemonAction { HandIndex = HandIndex },
                "PlayDomain" => new PlayDomainAction { HandIndex = HandIndex },
                "PlayMask" => new PlayMaskAction { HandIndex = HandIndex, TargetDaemonIndex = TargetIndex },
                "SetSeal" => new SetSealAction { HandIndex = HandIndex },
                "PlayDispel" => new PlayDispelAction
                {
                    HandIndex = HandIndex,
                    TargetType = ParseEnum<DispelTarget>(DispelTarget),
                    TargetIndex = TargetIndex,
                },
                "Evolve" => new EvolveAction { FieldIndex = FieldIndex, ConsumeIndex = ConsumeIndex },
                "Attack" => new AttackAction
                {
                    AttackerIndex = FieldIndex,
                    Target = ParseEnum<TargetType>(TargetType),
                    TargetIndex = TargetIndex,
                },
                "ActivatePillar" => new ActivatePillarAction { PillarIndex = PillarIndex, AbilityIndex = AbilityIndex },
                "NextPhase" => new NextPhaseAction(),
                "EndTurn" => new EndTurnAction(),
                _ => null,
            };
        }

        /// <summary>Create from a GameAction.</summary>
        public static SerializableAction FromGameAction(GameAction action)
        {
            var sa = new SerializableAction { ActionType = action.Type.ToString() };
            switch (action)
            {
                case PlayDaemonAction pda: sa.HandIndex = pda.HandIndex; break;
                case PlayDomainAction pdo: sa.HandIndex = pdo.HandIndex; break;
                case PlayMaskAction pma: sa.HandIndex = pma.HandIndex; sa.TargetIndex = pma.TargetDaemonIndex; break;
                case SetSealAction ssa: sa.HandIndex = ssa.HandIndex; break;
                case PlayDispelAction pdi: sa.HandIndex = pdi.HandIndex; sa.TargetIndex = pdi.TargetIndex; sa.DispelTarget = pdi.TargetType.ToString(); break;
                case EvolveAction ea: sa.FieldIndex = ea.FieldIndex; sa.ConsumeIndex = ea.ConsumeIndex; break;
                case AttackAction aa: sa.FieldIndex = aa.AttackerIndex; sa.TargetIndex = aa.TargetIndex; sa.TargetType = aa.Target.ToString(); break;
                case ActivatePillarAction apa: sa.PillarIndex = apa.PillarIndex; sa.AbilityIndex = apa.AbilityIndex; break;
            }
            return sa;
        }

        private static T ParseEnum<T>(string value) where T : struct
        {
            if (string.IsNullOrEmpty(value)) return default;
            Enum.TryParse<T>(value, true, out var result);
            return result;
        }
    }

    /// <summary>
    /// Serializable snapshot of the full game state.
    /// This is what travels over the wire as the
    /// "single source of truth" from the server.
    /// </summary>
    [Serializable]
    public class SerializableGameState
    {
        public string RoomId;
        public int CurrentPlayer;
        public string Phase;
        public int TurnNumber;
        public bool GameOver;
        public int Winner;
        public SerializablePlayerState[] Players;
        public string ActiveDomainId;
        public int ActiveDomainOwner;
        public List<SerializableLogEntry> RecentLog;
    }

    [Serializable]
    public class SerializablePlayerState
    {
        public string Id;
        public string Name;
        public int ConjurorHp;
        public int ConjurorMaxHp;
        public int Will;
        public int MaxWill;
        public int HandCount;       // opponent sees count, not cards
        public int DeckCount;
        public string[] HandCardIds; // only sent to the owning player
        public SerializableDaemon[] Field;
        public SerializablePillar[] Pillars;
        public int SealCount;       // opponent sees count
        public int AshePileCount;
    }

    [Serializable]
    public class SerializableDaemon
    {
        public string InstanceId;
        public string CardId;
        public int CurrentAshe;
        public int MaxAshe;
        public int Attack;
        public bool CanAttack;
        public bool HasAttacked;
        public bool Frozen;
        public bool Stealthed;
        public bool Entangled;
        public bool HasTaunt;
        public int ShieldAmount;
        public int ThornsDamage;
        public bool Silenced;
        public string[] MaskIds;
    }

    [Serializable]
    public class SerializablePillar
    {
        public string InstanceId;
        public string CardId;
        public int CurrentHp;
        public int MaxHp;
        public int Loyalty;
        public bool Destroyed;
        public bool Revealed;
    }

    [Serializable]
    public class SerializableLogEntry
    {
        public int Turn;
        public int Player;
        public string Message;
        public string Type;
    }

    /// <summary>
    /// Complete replay data for a finished game.
    /// "Store move histories for auditing disputes."
    /// </summary>
    [Serializable]
    public class ReplayData
    {
        public string RoomId;
        public string GameMode;
        public long StartTime;
        public long EndTime;
        public string[] PlayerNames;
        public string[] DeckIds;
        public int WinnerIndex;
        public string WinReason;
        public int ShuffleSeed;     // for deterministic replay
        public List<ReplayEntry> Moves;
    }

    [Serializable]
    public class ReplayEntry
    {
        public int TurnNumber;
        public int PlayerIndex;
        public long Timestamp;
        public SerializableAction Action;
        public bool Success;
        public string Reason;
    }
}
