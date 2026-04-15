// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Game Core (Pure Logic API)
//
//  "The Game Core needs to be clean and simple and needs
//   to work in its own universe without dependencies."
//  — FelipeRes, Card Games Programming 1
//
//  This is the public-facing API for Dual Craft battles.
//  It validates actions, delegates to BattleManager,
//  returns typed results, and maintains a persistent log.
//  Zero Unity dependencies — portable and unit-testable.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace DualCraft.Battle
{
    using Core;

    /// <summary>
    /// Result of an action attempt. Contains success/failure,
    /// reason string, and the resulting game state snapshot.
    /// </summary>
    public class ActionResult
    {
        public bool Success;
        public string Reason;
        public int PlayerIndex;
        public ActionType ActionType;
        public GameState State;

        public static ActionResult Ok(int player, ActionType type, GameState state) => new()
        {
            Success = true,
            Reason = "",
            PlayerIndex = player,
            ActionType = type,
            State = state,
        };

        public static ActionResult Fail(int player, ActionType type, string reason, GameState state) => new()
        {
            Success = false,
            Reason = reason,
            PlayerIndex = player,
            ActionType = type,
            State = state,
        };
    }

    /// <summary>
    /// The public Game Core API. This is the single entry point
    /// for all game interactions. It wraps BattleManager with:
    ///
    ///   1. Input validation (is it your turn? is the action valid?)
    ///   2. Action execution (delegates to BattleManager)
    ///   3. Result reporting (typed ActionResult with reason)
    ///   4. Event broadcasting (state changes, log entries, game over)
    ///   5. Persistent game log (every action recorded)
    ///
    /// The core has zero Unity dependencies. It works on its own.
    /// Visual representation wraps around it.
    /// </summary>
    public class GameCore
    {
        private readonly BattleManager _engine;

        /// <summary>Current game state (read-only view).</summary>
        public GameState State => _engine.State;

        /// <summary>The full action log for this game.</summary>
        public List<ActionResult> ActionLog { get; } = new();

        /// <summary>Fired after every successful action.</summary>
        public event Action<ActionResult> OnActionProcessed;

        /// <summary>Fired when the game ends.</summary>
        public event Action<int, string> OnGameOver;

        /// <summary>Fired on every state change (pass-through from engine).</summary>
        public event Action<GameState> OnStateChanged;

        /// <summary>Fired on each log message (pass-through from engine).</summary>
        public event Action<LogEntry> OnLogEntry;

        /// <summary>
        /// Dice roll request — the only point where UI interaction
        /// is needed. Wire this to your visual layer.
        /// </summary>
        public event Action<string, int, Action<int, bool>> OnDiceRollRequested;

        /// <summary>Direct access to the underlying engine (for effect resolver etc.)</summary>
        public BattleManager Engine => _engine;

        public GameCore(BattleManager engine)
        {
            _engine = engine;
            _engine.OnStateChanged += state => OnStateChanged?.Invoke(state);
            _engine.OnLogEntry += entry => OnLogEntry?.Invoke(entry);
            _engine.OnGameOver += (winner, msg) => OnGameOver?.Invoke(winner, msg);
            _engine.OnDiceRollRequested += (name, threshold, cb) =>
                OnDiceRollRequested?.Invoke(name, threshold, cb);
        }

        /// <summary>
        /// The main API entry point. Validates and processes a player action.
        /// This is the equivalent of Game.Play(player, card) from the blog post.
        /// </summary>
        public ActionResult Play(int playerIndex, GameAction action)
        {
            // Pre-validation: game over?
            if (_engine.State.GameOver)
            {
                var fail = ActionResult.Fail(playerIndex, action.Type,
                    "Game is already over.", _engine.State);
                ActionLog.Add(fail);
                return fail;
            }

            // Pre-validation: is it this player's turn?
            if (playerIndex != _engine.State.CurrentPlayer)
            {
                var fail = ActionResult.Fail(playerIndex, action.Type,
                    $"Not your turn. Current player: {_engine.State.CurrentPlayer}.",
                    _engine.State);
                ActionLog.Add(fail);
                return fail;
            }

            // Pre-validation: phase-specific checks
            string phaseError = ValidatePhase(action);
            if (phaseError != null)
            {
                var fail = ActionResult.Fail(playerIndex, action.Type,
                    phaseError, _engine.State);
                ActionLog.Add(fail);
                return fail;
            }

            // Execute via engine
            bool success = _engine.ProcessAction(playerIndex, action);

            ActionResult result;
            if (success)
            {
                result = ActionResult.Ok(playerIndex, action.Type, _engine.State);
                // Check win conditions after every successful action
                _engine.CheckWinConditions();
            }
            else
            {
                result = ActionResult.Fail(playerIndex, action.Type,
                    "Action rejected by engine (invalid target, insufficient resources, etc.).",
                    _engine.State);
            }

            ActionLog.Add(result);
            OnActionProcessed?.Invoke(result);
            return result;
        }

        /// <summary>
        /// Validate that the action is appropriate for the current phase.
        /// Returns null if valid, or an error string if not.
        /// </summary>
        private string ValidatePhase(GameAction action)
        {
            var phase = _engine.State.Phase;
            return action switch
            {
                DrawCardAction when phase != GamePhase.Draw =>
                    $"Can only draw during Draw phase. Current: {phase}.",

                PlayDaemonAction when phase != GamePhase.Main1 && phase != GamePhase.Main2 =>
                    $"Can only play daemons during Main phases. Current: {phase}.",

                PlayDomainAction when phase != GamePhase.Main1 && phase != GamePhase.Main2 =>
                    $"Can only play domains during Main phases. Current: {phase}.",

                PlayMaskAction when phase != GamePhase.Main1 && phase != GamePhase.Main2 =>
                    $"Can only play masks during Main phases. Current: {phase}.",

                SetSealAction when phase != GamePhase.Main1 && phase != GamePhase.Main2 =>
                    $"Can only set seals during Main phases. Current: {phase}.",

                PlayDispelAction when phase != GamePhase.Main1 && phase != GamePhase.Main2 =>
                    $"Can only play dispels during Main phases. Current: {phase}.",

                AttackAction when phase != GamePhase.Battle =>
                    $"Can only attack during Battle phase. Current: {phase}.",

                _ => null, // valid
            };
        }

        /// <summary>Get the player whose turn it is.</summary>
        public PlayerState CurrentPlayer => _engine.State.Players[_engine.State.CurrentPlayer];

        /// <summary>Get the opponent.</summary>
        public PlayerState Opponent => _engine.State.Players[1 - _engine.State.CurrentPlayer];

        /// <summary>Is the game still running?</summary>
        public bool IsGameOver => _engine.State.GameOver;

        /// <summary>How many actions have been processed this game?</summary>
        public int ActionCount => ActionLog.Count;

        /// <summary>How many successful actions?</summary>
        public int SuccessfulActions => ActionLog.FindAll(a => a.Success).Count;
    }
}
