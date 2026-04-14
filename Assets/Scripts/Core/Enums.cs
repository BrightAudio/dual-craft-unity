// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Core Enums
// ═══════════════════════════════════════════════════════

namespace DualCraft.Core
{
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

    public enum CreatureType
    {
        Elemental,
        Machine,
        Artificial,
        Spirit,
        Undead
    }

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

    public enum Rarity
    {
        Common,
        Rare,
        Epic,
        Legendary
    }

    public enum GamePhase
    {
        Draw,
        Main1,
        Battle,
        Main2,
        End
    }

    public enum AbilityType
    {
        Passive,
        OnSummon,
        OnDestroy
    }

    public enum SealTrigger
    {
        OnAttack,
        OnSummon,
        OnDaemonDestroy,
        OnSpell
    }

    public enum DispelTarget
    {
        Domain,
        Mask,
        Seal,
        Any
    }

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

    public enum MaskEffectType
    {
        AtkBoost,
        AsheBoost,
        Haste,
        Stealth,
        Thorns,
        Entangle
    }

    public enum SealEffectType
    {
        Drain,
        Destroy,
        Negate,
        CounterSpell,
        HealConjuror
    }

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

    public enum TargetType
    {
        Daemon,
        Pillar,
        Conjuror
    }

    public enum LogEntryType
    {
        Action,
        Effect,
        Combat,
        System
    }
}
