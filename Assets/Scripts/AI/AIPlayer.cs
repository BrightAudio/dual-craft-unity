// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — AI Opponent
//  Evaluates board state and picks best actions
// ═══════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DualCraft.AI
{
    using Battle;
    using Cards;
    using Core;

    public class AIPlayer
    {
        public enum Difficulty { Easy, Normal, Hard }

        private readonly int _playerIndex;
        private readonly Difficulty _difficulty;

        public AIPlayer(int playerIndex, Difficulty difficulty = Difficulty.Normal)
        {
            _playerIndex = playerIndex;
            _difficulty = difficulty;
        }

        public List<GameAction> DecideActions(GameState state)
        {
            var actions = new List<GameAction>();
            var player = state.Players[_playerIndex];

            switch (state.Phase)
            {
                case GamePhase.Draw:
                    actions.Add(new DrawCardAction());
                    break;

                case GamePhase.Main1:
                case GamePhase.Main2:
                    actions.AddRange(DecideMainPhase(state, player));
                    actions.Add(new NextPhaseAction());
                    break;

                case GamePhase.Battle:
                    actions.AddRange(DecideBattlePhase(state, player));
                    actions.Add(new NextPhaseAction());
                    break;

                case GamePhase.End:
                    actions.Add(new EndTurnAction());
                    break;
            }

            return actions;
        }

        private List<GameAction> DecideMainPhase(GameState state, PlayerState player)
        {
            var actions = new List<GameAction>();
            var opponent = state.Players[1 - _playerIndex];

            // Play daemons (prioritize by attack power for aggressive AI)
            for (int i = player.Hand.Count - 1; i >= 0; i--)
            {
                if (player.Field.Count >= GameConstants.MaxFieldDaemons) break;
                var card = player.Hand[i].Card;
                if (card is not DaemonCardData daemon) continue;
                if (player.Will < card.GetWillCost()) continue;

                // Easy AI: random chance to skip
                if (_difficulty == Difficulty.Easy && Random.value > 0.6f) continue;

                actions.Add(new PlayDaemonAction { HandIndex = i });
                player.Will -= card.GetWillCost(); // Track spend for this planning pass
                break; // Play one at a time
            }

            // Play masks on strongest daemon
            if (player.Field.Count > 0)
            {
                for (int i = player.Hand.Count - 1; i >= 0; i--)
                {
                    var card = player.Hand[i].Card;
                    if (card is not MaskCardData mask) continue;
                    if (player.Will < card.GetWillCost()) continue;

                    int bestDaemon = FindStrongestDaemon(player);
                    if (bestDaemon >= 0)
                    {
                        actions.Add(new PlayMaskAction { HandIndex = i, TargetDaemonIndex = bestDaemon });
                        break;
                    }
                }
            }

            // Set seals
            if (player.SealZone.Count < GameConstants.MaxSeals)
            {
                for (int i = player.Hand.Count - 1; i >= 0; i--)
                {
                    var card = player.Hand[i].Card;
                    if (card is not SealCardData seal) continue;
                    if (player.Will < card.GetWillCost()) continue;

                    actions.Add(new SetSealAction { HandIndex = i });
                    break;
                }
            }

            // Activate pillar abilities if worthwhile
            if (_difficulty >= Difficulty.Normal)
            {
                for (int p = 0; p < player.Pillars.Count; p++)
                {
                    var pillar = player.Pillars[p];
                    if (pillar.Destroyed || pillar.AbilityUsedThisTurn) continue;
                    if (pillar.Card.activatedAbilities == null) continue;

                    for (int a = 0; a < pillar.Card.activatedAbilities.Length; a++)
                    {
                        if (pillar.Loyalty >= pillar.Card.activatedAbilities[a].loyaltyCost)
                        {
                            actions.Add(new ActivatePillarAction { PillarIndex = p, AbilityIndex = a });
                            break;
                        }
                    }
                }
            }

            return actions;
        }

        private List<GameAction> DecideBattlePhase(GameState state, PlayerState player)
        {
            var actions = new List<GameAction>();
            var opponent = state.Players[1 - _playerIndex];

            for (int i = 0; i < player.Field.Count; i++)
            {
                var daemon = player.Field[i];
                if (!daemon.CanAttack || daemon.HasAttacked || daemon.Frozen) continue;

                var target = ChooseAttackTarget(daemon, opponent);
                actions.Add(new AttackAction
                {
                    AttackerIndex = i,
                    Target = target.type,
                    TargetIndex = target.index,
                });
            }

            return actions;
        }

        private (TargetType type, int index) ChooseAttackTarget(DaemonInstance attacker, PlayerState opponent)
        {
            // Hard AI: prioritize element advantages
            if (_difficulty == Difficulty.Hard && opponent.Field.Count > 0)
            {
                for (int i = 0; i < opponent.Field.Count; i++)
                {
                    if (ElementSystem.IsAdvantaged(attacker.Card.element, opponent.Field[i].Card.element))
                    {
                        return (TargetType.Daemon, i);
                    }
                }
            }

            // Kill low-HP daemons first
            if (opponent.Field.Count > 0)
            {
                int weakest = 0;
                int lowestHp = int.MaxValue;
                for (int i = 0; i < opponent.Field.Count; i++)
                {
                    if (opponent.Field[i].CurrentAshe < lowestHp)
                    {
                        lowestHp = opponent.Field[i].CurrentAshe;
                        weakest = i;
                    }
                }

                // If we can kill it, do it
                if (lowestHp <= attacker.Attack)
                    return (TargetType.Daemon, weakest);
            }

            // If field is clear, go for conjuror or pillars
            if (opponent.Field.Count == 0)
            {
                // Attack weakest pillar if any alive
                for (int i = 0; i < opponent.Pillars.Count; i++)
                {
                    if (!opponent.Pillars[i].Destroyed)
                        return (TargetType.Pillar, i);
                }
                return (TargetType.Conjuror, 0);
            }

            // Default: attack the weakest enemy daemon
            if (opponent.Field.Count > 0)
                return (TargetType.Daemon, 0);

            return (TargetType.Conjuror, 0);
        }

        private int FindStrongestDaemon(PlayerState player)
        {
            int best = -1;
            int bestAtk = -1;
            for (int i = 0; i < player.Field.Count; i++)
            {
                if (player.Field[i].Attack > bestAtk)
                {
                    bestAtk = player.Field[i].Attack;
                    best = i;
                }
            }
            return best;
        }
    }
}
