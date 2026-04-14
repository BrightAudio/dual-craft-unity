// ═══════════════════════════════════════════════════════
// DUAL CRAFT — Core Enums
// Defines all enumerations used across the game logic.
// ═══════════════════════════════════════════════════════

namespace DualCraft.Core
{
    // Elements to which daemons and spells may belong
    public enum Element
    {
        Flame,
        Ice,
        Water,
        Earth,
        Air,
        Light,
        Dark,
        Nature
    }

    // Creature types used for secondary matchups
    public enum CreatureType
    {
        Elemental,
        Machine,
        Artificial,
        Spirit,
        Undead
    }

    // Card categories (the 7 card types)
    public enum CardCategory
    {
        Daemon,
        Pillar,
        Domain,
        Mask,
        Seal,
        Dispel,
        Conjuror
    }

    // Rarity tiers for cards
    public enum Rarity
    {
        Common,
        Rare,
        Epic,
        Legendary
    }

    // Represents the different phases of a player's turn
    public enum GamePhase
    {
        Draw,
        Main1,
        Battle,
        Main2,
        End
    }

    // Daemon ability trigger types
    public enum AbilityType
    {
        Passive,
        OnSummon,
        OnDestroy
    }

    // When a seal trap activates
    public enum SealTrigger
    {
        OnAttack,
        OnSummon,
        OnDaemonDestroy,
        OnSpell
    }

    // Used for dispel actions to determine what effect is being removed
    public enum DispelTarget
    {
        Domain,
        Mask,
        Seal,
        Any
    }

    // Domain spell effect types
    public enum DomainEffectType
    {
        AtkBuffAll,
        DamageAllEnd,
        Protection,
        ElementAtkBuff,
        ExtraDraw,
        PillarRestore,
        PillarHeal
    }

    // Mask equipment effect types
    public enum MaskEffectType
    {
        AtkBoost,
        AsheBoost,
        Haste,
        Stealth,
        Thorns,
        Entangle
    }

    // Seal trap effect types
    public enum SealEffectType
    {
        Drain,
        Destroy,
        Negate,
        CounterSpell,
        HealConjuror
    }

    // Identifies the type of action being processed
    public enum ActionType
    {
        DrawCard,
        PlayDaemon,
        PlayDomain,
        PlayMask,
        SetSeal,
        PlayDispel,
        Evolve,
        Attack,
        ActivatePillar,
        NextPhase,
        EndTurn
    }

    // Specifies what target the attacker is aiming at
    public enum TargetType
    {
        Daemon,
        Pillar,
        Conjuror
    }

    // Categories for logging entries
    public enum LogEntryType
    {
        Action,
        Effect,
        Combat,
        System
    }
}