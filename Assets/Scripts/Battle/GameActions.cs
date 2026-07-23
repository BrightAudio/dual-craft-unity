// ═══════════════════════════════════════════════════════
// DUAL CRAFT — Game Actions (Command Pattern)
// Defines lightweight action objects passed to BattleManager for processing.
// ═══════════════════════════════════════════════════════

namespace DualCraft.Battle
{
    using System;
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
        public int TargetLane = -1;
    }

    public class PlayDomainAction : GameAction
    {
        public override ActionType Type => ActionType.PlayDomain;
        public int HandIndex;
        public int ResponseDispelHandIndex = -1;
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

    public class PlayHexAction : GameAction
    {
        public override ActionType Type => ActionType.PlayHex;
        public int HandIndex;
        public int TargetDaemonIndex = -1;
        public int ResponseDispelHandIndex = -1;
    }

    public class EvolveAction : GameAction
    {
        public override ActionType Type => ActionType.Evolve;
        public int FieldIndex;
        public int ConsumeIndex;
        public int[] AnchorFieldIndices = Array.Empty<int>();
    }

    public class AttackAction : GameAction
    {
        public override ActionType Type => ActionType.Attack;
        public int AttackerIndex;
        public TargetType Target;
        public int TargetIndex;
        /// <summary>
        /// Optional defender hand index for an attack-specific Dispel response.
        /// Used by the UI reaction window before damage resolves.
        /// </summary>
        public int ResponseDispelHandIndex = -1;
    }

    public class FuseDaemonsAction : GameAction
    {
        public override ActionType Type => ActionType.FuseDaemons;
        public int PrimaryIndex;
        public int SecondaryIndex;
    }

    public class ActivatePillarAction : GameAction
    {
        public override ActionType Type => ActionType.ActivatePillar;
        public int PillarIndex;
        public int AbilityIndex;
    }

    public class ActivateInvokerAction : GameAction
    {
        public override ActionType Type => ActionType.ActivateInvoker;
    }

    public class NextPhaseAction : GameAction
    {
        public override ActionType Type => ActionType.NextPhase;
    }

    public class EndTurnAction : GameAction
    {
        public override ActionType Type => ActionType.EndTurn;
    }

    /// <summary>Play an Ashe card from hand and assign it to a field daemon.</summary>
    public class PlayAsheCardAction : GameAction
    {
        public override ActionType Type => ActionType.PlayAsheCard;
        /// <summary>Index of the Ashe card in the player's hand.</summary>
        public int HandIndex;
        /// <summary>Index of the target daemon in the player's field.</summary>
        public int TargetDaemonFieldIndex;
    }

    /// <summary>Reassign an unassigned Ashe card on the board to a different daemon.</summary>
    public class AssignAsheCardAction : GameAction
    {
        public override ActionType Type => ActionType.AssignAsheCard;
        /// <summary>Index in PlayerState.AsheCards list.</summary>
        public int AsheCardBoardIndex;
        /// <summary>Index of the target daemon in the player's field.</summary>
        public int TargetDaemonFieldIndex;
    }

    /// <summary>Raid an exposed enemy Source during combat, suppressing its next turn income.</summary>
    public class AttackAsheCardAction : GameAction
    {
        public override ActionType Type => ActionType.AttackAsheCard;
        /// <summary>Index of the attacker in the player's field.</summary>
        public int AttackerFieldIndex;
        /// <summary>Index in the opponent's PlayerState.AsheCards list.</summary>
        public int AsheCardBoardIndex;
    }

    /// <summary>Voluntarily send an allied daemon to the Void for Spirit Energy.</summary>
    public class SacrificeDaemonAction : GameAction
    {
        public override ActionType Type => ActionType.SacrificeDaemon;
        public int FieldIndex;
    }

    /// <summary>Spend a daemon's attack turn to move it to another board lane.</summary>
    public class SwitchLaneAction : GameAction
    {
        public override ActionType Type => ActionType.SwitchLane;
        public int FieldIndex;
        public int TargetLane;
    }
}
