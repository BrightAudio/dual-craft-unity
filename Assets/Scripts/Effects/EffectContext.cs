// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Effect Context
//  The "row" of data passed to every effect function.
//  Contains everything the function needs to resolve.
// ═══════════════════════════════════════════════════════

using DualCraft.Battle;
using DualCraft.Cards;

namespace DualCraft.Effects
{
    /// <summary>
    /// Snapshot of the context in which an effect is being executed.
    /// Effect functions receive this plus their int[] params — they
    /// never need to reach outside this struct for game state.
    /// </summary>
    public class EffectContext
    {
        /// <summary>Full game state (mutable — effects modify it directly).</summary>
        public GameState State;

        /// <summary>Index of the player who owns the card triggering this effect.</summary>
        public int OwnerIndex;

        /// <summary>Convenience: State.Players[OwnerIndex].</summary>
        public PlayerState Owner => State.Players[OwnerIndex];

        /// <summary>Convenience: State.Players[1 - OwnerIndex].</summary>
        public PlayerState Opponent => State.Players[1 - OwnerIndex];

        /// <summary>The daemon executing the effect (null if source is not a daemon).</summary>
        public DaemonInstance SourceDaemon;

        /// <summary>The pillar executing the effect (null if source is not a pillar).</summary>
        public PillarInstance SourcePillar;

        /// <summary>The seal that fired (null if not a seal trigger).</summary>
        public SealInstance SourceSeal;

        /// <summary>The card data that produced this effect.</summary>
        public CardData SourceCard;

        /// <summary>Target daemon chosen by the player (for TargetDaemon effects).</summary>
        public DaemonInstance TargetDaemon;

        /// <summary>Target pillar (if applicable).</summary>
        public PillarInstance TargetPillar;

        /// <summary>Index of the target in the appropriate list.</summary>
        public int TargetIndex = -1;

        /// <summary>Whether the target belongs to the owner (true) or opponent (false).</summary>
        public bool TargetIsFriendly;
    }

    /// <summary>
    /// Complete description of one effect on a card — this is what gets
    /// stored in the database / JSON / ScriptableObject. One card can
    /// have multiple EffectEntry instances (e.g., OnSummon + Passive).
    /// </summary>
    [System.Serializable]
    public class EffectEntry
    {
        /// <summary>When does this effect fire?</summary>
        public EffectTrigger trigger;

        /// <summary>What does it target?</summary>
        public EffectTarget target;

        /// <summary>Which function to call (the integer lookup).</summary>
        public EffectFunctionId functionId;

        /// <summary>
        /// Up to 5 parameters for the function. Same function + different
        /// params = completely different card. E.g., DealDamage with [3]
        /// vs [7] vs [3, 1] (element-conditional).
        /// </summary>
        public int[] parameters = new int[0];

        /// <summary>Human-readable description for UI tooltip.</summary>
        public string description;
    }
}
