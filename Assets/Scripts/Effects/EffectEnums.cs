// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Effect System Enums
//  Trigger/Target/Function decomposition so one function
//  can power dozens of cards by changing parameters.
// ═══════════════════════════════════════════════════════

namespace DualCraft.Effects
{
    /// <summary>When does this effect fire?</summary>
    public enum EffectTrigger
    {
        None = 0,
        OnSummon = 1,        // Daemon enters the field
        OnDestroy = 2,       // Card is destroyed
        OnAttack = 3,        // Daemon declares attack
        OnDamaged = 4,       // Daemon takes damage
        Passive = 5,         // Always active while on field
        Activated = 6,       // Costs loyalty/will to use
        OnTurnStart = 7,     // Owner's turn begins
        OnTurnEnd = 8,       // Owner's turn ends
        OnDraw = 9,          // Card is drawn
        OnPlay = 10,         // Any card is played
        OnSealTriggered = 11,// Seal trap fires
        OnPillarReveal = 12, // Pillar is flipped face-up
        OnEvolve = 13,       // Daemon evolves
        OnMaskEquipped = 14, // Mask is attached
        OnMaskExpired = 15,  // Mask duration runs out
    }

    /// <summary>What does this effect target?</summary>
    public enum EffectTarget
    {
        None = 0,
        Self = 1,                   // The card itself
        AllFriendlyDaemons = 2,     // All daemons on owner's field
        AllEnemyDaemons = 3,        // All daemons on opponent's field
        AllDaemons = 4,             // Every daemon on the board
        RandomEnemyDaemon = 5,      // Random enemy daemon
        RandomFriendlyDaemon = 6,   // Random friendly daemon
        FriendlyConjuror = 7,       // Owner's conjuror
        EnemyConjuror = 8,          // Opponent's conjuror
        TargetDaemon = 9,           // Player picks a daemon
        TargetEnemyDaemon = 10,     // Player picks an enemy daemon
        TargetFriendlyDaemon = 11,  // Player picks a friendly daemon
        AllFriendlyPillars = 12,    // All friendly pillars
        AllEnemyPillars = 13,       // All enemy pillars
        StrongestEnemy = 14,        // Highest-ATK enemy daemon
        WeakestEnemy = 15,          // Lowest-ATK enemy daemon
        AllPlayers = 16,            // Both conjurors
        AttackingDaemon = 17,       // The daemon that is attacking (seal use)
        TriggerSource = 18,         // Whatever triggered this effect
    }

    /// <summary>
    /// Function ID — the "column" from the database approach.
    /// Each ID maps to one reusable function in EffectFunctions.
    /// Change the parameters to make completely different cards
    /// from the same function. E.g. DealDamage with params [3]
    /// vs [7] is two different cards, same code.
    /// </summary>
    public enum EffectFunctionId
    {
        None = 0,

        // ─── Damage / Heal ───────────────────────────────
        DealDamage = 1,           // params: [amount]
        HealAshe = 2,             // params: [amount]
        HealConjuror = 3,         // params: [amount]
        DamageConjuror = 4,       // params: [amount]
        DrainAshe = 5,            // params: [amount] — deal damage, heal self same amount

        // ─── Stat Buffs ──────────────────────────────────
        BuffAttack = 10,          // params: [amount]
        BuffAshe = 11,            // params: [amount]
        DebuffAttack = 12,        // params: [amount]
        BuffAttackConditional = 13,// params: [amount, requiredElement]

        // ─── Status Effects ──────────────────────────────
        Freeze = 20,              // params: [turns]
        Stealth = 21,             // params: [turns]
        Entangle = 22,            // params: [turns]
        Shield = 23,              // params: [amount] — absorbs damage
        Thorns = 24,              // params: [damage] — reflect on hit
        Taunt = 25,               // params: [turns]

        // ─── Resource ────────────────────────────────────
        DrawCards = 30,           // params: [count]
        GainWill = 31,            // params: [amount]
        DrainWill = 32,           // params: [amount] — steal from opponent
        ReduceCost = 33,          // params: [amount] — reduce next card cost
        RestoreWill = 34,         // params: [amount]

        // ─── Board Control ───────────────────────────────
        DestroyTarget = 40,       // params: [] — destroy target card
        ReturnToHand = 41,        // params: [] — bounce target to hand
        Silence = 42,             // params: [] — remove abilities
        DestroyWeakest = 43,      // params: [maxAshe] — destroy if ashe <= threshold
        DestroyByElement = 44,    // params: [element] — destroy if matching element

        // ─── Pillar-specific ─────────────────────────────
        RestorePillarHp = 50,     // params: [amount]
        GainLoyalty = 51,         // params: [amount]
        PillarElementBuff = 52,   // params: [element, atkAmount]

        // ─── Domain-specific ─────────────────────────────
        AtkBuffAll = 60,          // params: [amount]
        DamageAllEnd = 61,        // params: [amount]
        ElementAtkBuff = 62,      // params: [element, amount]
        ExtraDraw = 63,           // params: [count]
        PillarHealDomain = 64,    // params: [amount]

        // ─── Seal-specific ───────────────────────────────
        NegateAction = 70,        // params: [] — cancel the triggering action
        CounterAndDamage = 71,    // params: [damage]
        SealDrain = 72,           // params: [amount] — drain HP from attacker

        // ─── Summoning ───────────────────────────────────
        SummonToken = 80,         // params: [tokenAtk, tokenAshe, count]

        // ─── Multi-effect (combo) ────────────────────────
        DamageAndDraw = 90,       // params: [damage, drawCount]
        BuffAndHeal = 91,         // params: [atkBuff, healAmount]
        DamageAllAndHealSelf = 92,// params: [damage, healAmount]
    }
}
