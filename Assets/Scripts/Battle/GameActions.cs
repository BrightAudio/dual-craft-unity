// ═══════════════════════════════════════════════════════
// DUAL CRAFT — Game Actions (Command Pattern)
// Defines lightweight action objects passed to BattleManager for processing.
// ═══════════════════════════════════════════════════════

namespace DualCraft.Battle
{
    using Core;

    public abstract class GameAction
    {
        public abstract ActionType Type { get; }
    }

    public class DrawCardAction : GameAction
    {
        public override ActionType Type => ActionType.DrawCard;
    }

    public class PlayDaemonAction : GameAction
    {
        public override ActionType Type => ActionType.PlayDaemon;
        public int HandIndex;
    }

    public class PlayDomainAction : GameAction
    {
        public override ActionType Type => ActionType.PlayDomain;
        public int HandIndex;
    }

    public class PlayMaskAction : GameAction
    {
        public override ActionType Type => ActionType.PlayMask;
        public int HandIndex;
        public int TargetDaemonIndex;
    }

    public class SetSealAction : GameAction
    {
        public override ActionType Type => ActionType.SetSeal;
        public int HandIndex;
    }

    public class PlayDispelAction : GameAction
    {
        public override ActionType Type => ActionType.PlayDispel;
        public int HandIndex;
        public DispelTarget TargetType;
        public int TargetIndex;
    }

    public class EvolveAction : GameAction
    {
        public override ActionType Type => ActionType.Evolve;
        public int FieldIndex;
        public int ConsumeIndex;
    }

    public class AttackAction : GameAction
    {
        public override ActionType Type => ActionType.Attack;
        public int AttackerIndex;
        public TargetType Target;
        public int TargetIndex;
    }

    public class ActivatePillarAction : GameAction
    {
        public override ActionType Type => ActionType.ActivatePillar;
        public int PillarIndex;
        public int AbilityIndex;
    }

    public class NextPhaseAction : GameAction
    {
        public override ActionType Type => ActionType.NextPhase;
    }

    public class EndTurnAction : GameAction
    {
        public override ActionType Type => ActionType.EndTurn;
    }
}