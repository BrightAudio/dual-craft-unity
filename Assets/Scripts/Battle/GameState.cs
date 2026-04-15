// ═══════════════════════════════════════════════════════
// DUAL CRAFT — Runtime Game State
// Holds runtime instances of cards on the field, in hand, and in other zones.
// ═══════════════════════════════════════════════════════
using System.Collections.Generic;

namespace DualCraft.Battle
{
    using Core;
    using Cards;

    /// <summary>
    /// Represents the complete state of an ongoing match.  Contains both
    /// players' states, current turn information, active domain effects and
    /// combat log.  This object is mutable and should only be modified via
    /// the BattleManager to maintain consistency with game rules.
    /// </summary>
    public class GameState
    {
        public string RoomId;
        public PlayerState[] Players = new PlayerState[2];
        public int CurrentPlayer;
        public GamePhase Phase;
        public int TurnNumber;
        public ActiveDomain ActiveDomain;
        public int? Winner;
        public bool GameOver;
        public List<LogEntry> Log = new();
        public string LastAction;
    }

    public class ActiveDomain
    {
        public DomainCardData Card;
        public int Owner;
    }

    /// <summary>
    /// Per‑player runtime state.  Tracks hand, deck, field, pillars, seal zone,
    /// ashe (discard) pile, current will, and max will.  Also stores the
    /// Conjuror's health and maximum health.
    /// </summary>
    public class PlayerState
    {
        public string Id;
        public string Name;
        public ConjurorState Conjuror = new();
        public List<PillarInstance> Pillars = new();
        public List<DaemonInstance> Field = new();
        public List<SealInstance> SealZone = new();
        public List<CardInstance> Hand = new();
        public List<CardInstance> Deck = new();
        public List<CardInstance> AshePile = new();
        public int Will;
        public int MaxWill;
        public int CostReduction;
        public int ExtraDraws;
    }

    public class ConjurorState
    {
        public int Hp;
        public int MaxHp;
    }

    public class DaemonInstance
    {
        public string InstanceId;
        public DaemonCardData Card;
        public int CurrentAshe;
        public int MaxAshe;
        public int Attack;
        public int AsheCost;
        public List<MaskInstance> Masks = new();
        public bool CanAttack;
        public bool HasAttacked;

        // Status effects with duration tracking
        public bool Frozen;
        public int FrozenTurns;
        public bool Stealthed;
        public int StealthTurns;
        public bool Entangled;
        public int EntangledTurns;
        public bool HasTaunt;
        public int TauntTurns;
        public int ShieldAmount;
        public int ThornsDamage;
        public bool Silenced;

        // ─── New statuses (poketcg-inspired) ─────────
        /// <summary>Poison: takes damage each turn at end of turn.</summary>
        public bool Poisoned;
        public int PoisonDamage;   // default 1, double-poison = 2

        /// <summary>Burn: takes damage each turn + 50% chance to miss attack.</summary>
        public bool Burning;
        public int BurnDamage;     // default 1

        /// <summary>Damage reduction shield (flat reduction per hit, not absorb).</summary>
        public int DamageReduction;
        public int DamageReductionTurns;

        /// <summary>Next attack deals double damage (like Swords Dance).</summary>
        public bool NextAttackDouble;
    }

    public class PillarInstance
    {
        public string InstanceId;
        public PillarCardData Card;
        public int CurrentHp;
        public int MaxHp;
        public int Loyalty;
        public bool Destroyed;
        public bool Revealed;
        public bool AbilityUsedThisTurn;
    }

    public class MaskInstance
    {
        public MaskCardData Card;
        public int TurnsRemaining;
    }

    public class SealInstance
    {
        public string InstanceId;
        public SealCardData Card;
    }

    public class CardInstance
    {
        public string InstanceId;
        public CardData Card;
    }

    public class LogEntry
    {
        public int Turn;
        public int Player;
        public string Message;
        public LogEntryType Type;
    }
}