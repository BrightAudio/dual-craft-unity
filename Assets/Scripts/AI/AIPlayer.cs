// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — AI Opponent
//  Inspired by Card-Forge architecture:
//  - Score-based daemon evaluation (CreatureEvaluator)
//  - Board advantage / aggression calculation
//  - Profitable trade detection
//  - Multi-card sequencing per phase
//  - Threat assessment for targeting
// ═══════════════════════════════════════════════════════

using System;
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

        // Aggression: 0 = defensive, 1 = cautious, 2 = balanced, 3 = aggressive, 4 = all-in
        private int _aggression;

        public AIPlayer(int playerIndex, Difficulty difficulty = Difficulty.Normal)
        {
            _playerIndex = playerIndex;
            _difficulty = difficulty;
        }

        // ─── Daemon Evaluator (forge: CreatureEvaluator) ─────
        // Score a daemon for board-state comparison. Higher = more valuable.
        public static int EvaluateDaemon(DaemonInstance d)
        {
            int value = 80;
            value += d.Attack * 15;
            value += d.CurrentAshe * 10;
            value += d.MaxAshe * 3; // potential

            // Evasion / status bonuses
            if (d.Stealthed) value += d.Attack * 10;
            if (d.HasTaunt) value += 30;
            if (d.ShieldAmount > 0) value += d.ShieldAmount * 12;
            if (d.ThornsDamage > 0) value += d.ThornsDamage * 10;
            if (d.DamageReduction > 0) value += d.DamageReduction * 8;
            if (d.NextAttackDouble) value += d.Attack * 15;

            // Masks add value
            value += d.Masks.Count * 25;

            // Summoning sickness penalty
            if (!d.CanAttack) value -= 20;
            // Debuff penalties
            if (d.Frozen) value -= 40;
            if (d.Entangled) value -= 30;
            if (d.Silenced) value -= 20;
            // poketcg-inspired: poison/burn reduce value
            if (d.Poisoned) value -= d.PoisonDamage * 15;
            if (d.Burning) value -= d.BurnDamage * 20; // burn is worse (miss chance)

            return value;
        }

        public static int EvaluateCard(CardData card)
        {
            int value = 50;
            if (card is DaemonCardData daemon)
            {
                value += daemon.attack * 15 + daemon.ashe * 10;
            }
            else if (card is MaskCardData) value += 40;
            else if (card is DomainCardData) value += 35;
            else if (card is SealCardData) value += 30;
            else if (card is DispelCardData) value += 25;
            return value;
        }

        // ─── Board Advantage (forge: AiAttackController aggression) ─
        private void CalculateAggression(GameState state)
        {
            var me = state.Players[_playerIndex];
            var opp = state.Players[1 - _playerIndex];

            int myBoardValue = me.Field.Sum(d => EvaluateDaemon(d));
            int oppBoardValue = opp.Field.Sum(d => EvaluateDaemon(d));
            int myPillarCount = me.Pillars.Count(p => !p.Destroyed);
            int oppPillarCount = opp.Pillars.Count(p => !p.Destroyed);

            float hpRatio = (float)me.Conjuror.Hp / Mathf.Max(1, opp.Conjuror.Hp);
            float boardRatio = myBoardValue / Mathf.Max(1f, oppBoardValue);

            // Base aggression from board state
            if (boardRatio > 2f && hpRatio > 0.8f)
                _aggression = 4; // Dominating — go all in
            else if (boardRatio > 1.3f)
                _aggression = 3; // Ahead — attack aggressively
            else if (boardRatio > 0.7f)
                _aggression = 2; // Even — balanced play
            else if (boardRatio > 0.4f)
                _aggression = 1; // Behind — cautious
            else
                _aggression = 0; // Far behind — defend, build board

            // If opponent is low HP and we have field, be aggressive
            if (opp.Conjuror.Hp <= 10 && me.Field.Count > 0)
                _aggression = Mathf.Max(_aggression, 3);

            // If we're low HP, be more cautious unless we can lethal
            if (me.Conjuror.Hp <= 8 && boardRatio < 1.5f)
                _aggression = Mathf.Min(_aggression, 1);

            // Difficulty modifier
            if (_difficulty == Difficulty.Easy) _aggression = Mathf.Min(_aggression, 2);
        }

        // ─── Main Decision Entry Point ──────────────────────
        public List<GameAction> DecideActions(GameState state)
        {
            var actions = new List<GameAction>();
            var player = state.Players[_playerIndex];

            CalculateAggression(state);

            switch (state.Phase)
            {
                case GamePhase.Draw:
                    actions.Add(new DrawCardAction());
                    break;

                case GamePhase.Main1:
                    actions.AddRange(DecideMain1(state, player));
                    actions.Add(new NextPhaseAction());
                    break;

                case GamePhase.Battle:
                    actions.AddRange(DecideBattlePhase(state, player));
                    actions.Add(new NextPhaseAction());
                    break;

                case GamePhase.Main2:
                    actions.AddRange(DecideMain2(state, player));
                    actions.Add(new NextPhaseAction());
                    break;

                case GamePhase.End:
                    actions.Add(new EndTurnAction());
                    break;
            }

            return actions;
        }

        // ─── Main Phase 1: Play curve creatures ─────────────
        // Forge pattern: play creatures in Main1 for combat,
        // hold utility/masks for Main2 when possible.
        private List<GameAction> DecideMain1(GameState state, PlayerState player)
        {
            var actions = new List<GameAction>();
            var opponent = state.Players[1 - _playerIndex];
            int willBudget = player.Will;

            // Play domains first (global effects benefit the whole turn)
            for (int i = player.Hand.Count - 1; i >= 0; i--)
            {
                if (player.Hand[i].Card is not DomainCardData domain) continue;
                int cost = domain.GetWillCost();
                if (willBudget < cost) continue;
                actions.Add(new PlayDomainAction { HandIndex = i });
                willBudget -= cost;
                break; // one domain at a time
            }

            // Play daemons — sorted by best value-per-cost
            var daemonPlays = new List<(int index, int score, int cost)>();
            for (int i = 0; i < player.Hand.Count; i++)
            {
                if (player.Hand[i].Card is not DaemonCardData daemon) continue;
                int cost = daemon.GetWillCost();
                if (willBudget < cost) continue;
                if (player.Field.Count >= GameConstants.MaxFieldDaemons) continue;

                int score = daemon.attack * 15 + daemon.ashe * 10;
                // Bonus for element advantage against opponent's field
                if (_difficulty >= Difficulty.Normal && opponent.Field.Count > 0)
                {
                    foreach (var oppD in opponent.Field)
                    {
                        float matchup = ElementSystem.GetElementMatchup(daemon.element, oppD.Card.element);
                        if (matchup > 1f) score += 40;
                    }
                }
                daemonPlays.Add((i, score, cost));
            }

            // Sort by score descending, play as many as we can afford
            daemonPlays.Sort((a, b) => b.score.CompareTo(a.score));
            int fieldsUsed = player.Field.Count;
            foreach (var (index, score, cost) in daemonPlays)
            {
                if (willBudget < cost || fieldsUsed >= GameConstants.MaxFieldDaemons) break;

                // Easy AI: random chance to skip
                if (_difficulty == Difficulty.Easy && UnityEngine.Random.value > 0.6f) continue;

                actions.Add(new PlayDaemonAction { HandIndex = index });
                willBudget -= cost;
                fieldsUsed++;
            }

            // Set seals if we have spare will (set before combat)
            if (player.SealZone.Count < GameConstants.MaxSeals)
            {
                for (int i = player.Hand.Count - 1; i >= 0; i--)
                {
                    if (player.Hand[i].Card is not SealCardData) continue;
                    int cost = player.Hand[i].Card.GetWillCost();
                    if (willBudget < cost) continue;
                    actions.Add(new SetSealAction { HandIndex = i });
                    willBudget -= cost;
                    break;
                }
            }

            // Activate pillar abilities pre-combat (buff effects)
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

        // ─── Main Phase 2: Play masks, utility, leftover ────
        // Forge pattern: after combat, play buffs and remaining cards
        private List<GameAction> DecideMain2(GameState state, PlayerState player)
        {
            var actions = new List<GameAction>();
            var opponent = state.Players[1 - _playerIndex];
            int willBudget = player.Will;

            // Play masks on strongest daemon that survived combat
            if (player.Field.Count > 0)
            {
                for (int i = player.Hand.Count - 1; i >= 0; i--)
                {
                    if (player.Hand[i].Card is not MaskCardData mask) continue;
                    int cost = mask.GetWillCost();
                    if (willBudget < cost) continue;

                    int target = FindBestMaskTarget(player, mask);
                    if (target >= 0)
                    {
                        actions.Add(new PlayMaskAction { HandIndex = i, TargetDaemonIndex = target });
                        willBudget -= cost;
                        break;
                    }
                }
            }

            // Play any remaining daemons we couldn't afford in Main1
            for (int i = player.Hand.Count - 1; i >= 0; i--)
            {
                if (player.Field.Count >= GameConstants.MaxFieldDaemons) break;
                if (player.Hand[i].Card is not DaemonCardData daemon) continue;
                int cost = daemon.GetWillCost();
                if (willBudget < cost) continue;
                actions.Add(new PlayDaemonAction { HandIndex = i });
                willBudget -= cost;
            }

            // Use dispels if opponent has active domain or seals
            for (int i = player.Hand.Count - 1; i >= 0; i--)
            {
                if (player.Hand[i].Card is not DispelCardData dispel) continue;
                int cost = dispel.GetWillCost();
                if (willBudget < cost) continue;

                // Prioritize dispelling active domains
                if (state.ActiveDomain != null && state.ActiveDomain.Owner != _playerIndex)
                {
                    actions.Add(new PlayDispelAction
                    {
                        HandIndex = i,
                        TargetType = DispelTarget.Domain,
                        TargetIndex = 0,
                    });
                    willBudget -= cost;
                    break;
                }
                // Dispel opponent seals
                if (opponent.SealZone.Count > 0)
                {
                    actions.Add(new PlayDispelAction
                    {
                        HandIndex = i,
                        TargetType = DispelTarget.Seal,
                        TargetIndex = 0,
                    });
                    willBudget -= cost;
                    break;
                }
            }

            return actions;
        }

        // ─── Battle Phase: Smart Attacks ─────────────────────
        // Forge pattern: evaluate each potential attack for profitability.
        // Only send attackers when it's advantageous.
        private List<GameAction> DecideBattlePhase(GameState state, PlayerState player)
        {
            var actions = new List<GameAction>();
            var opponent = state.Players[1 - _playerIndex];

            // Gather all potential attackers
            var attackerCandidates = new List<int>();
            for (int i = 0; i < player.Field.Count; i++)
            {
                var d = player.Field[i];
                if (d.CanAttack && !d.HasAttacked && !d.Frozen && !d.Entangled)
                    attackerCandidates.Add(i);
            }

            if (attackerCandidates.Count == 0) return actions;

            // If opponent has no field, go face (following attack order rules)
            // Account for stealth: stealthed daemons don't block pillar/conjuror access
            bool opponentHasTargetable = opponent.Field.Any(d => !d.Stealthed);
            if (!opponentHasTargetable)
            {
                foreach (int atkIdx in attackerCandidates)
                {
                    bool pillarsAlive = opponent.Pillars.Any(p => !p.Destroyed);
                    if (pillarsAlive)
                    {
                        // Must attack pillars first — find weakest
                        int weakestPillar = FindWeakestPillar(opponent);
                        if (weakestPillar >= 0)
                        {
                            actions.Add(new AttackAction
                            {
                                AttackerIndex = atkIdx,
                                Target = TargetType.Pillar,
                                TargetIndex = weakestPillar,
                            });
                        }
                    }
                    else
                    {
                        // No pillars — go for conjuror
                        actions.Add(new AttackAction
                        {
                            AttackerIndex = atkIdx,
                            Target = TargetType.Conjuror,
                            TargetIndex = 0,
                        });
                    }
                }
                return actions;
            }

            // Opponent has daemons — evaluate profitable attacks
            // (forge pattern: evaluate trades before declaring)
            foreach (int atkIdx in attackerCandidates)
            {
                var attacker = player.Field[atkIdx];
                var target = ChooseSmartTarget(attacker, opponent);

                // Based on aggression, decide whether to send this attacker
                bool shouldAttack = target.profitable || _aggression >= 3;
                if (_aggression >= 2 && target.evenTrade) shouldAttack = true;
                if (_aggression <= 0 && !target.profitable) shouldAttack = false;

                // Easy AI: sometimes makes suboptimal attacks
                if (_difficulty == Difficulty.Easy && UnityEngine.Random.value > 0.7f)
                    shouldAttack = true;

                if (shouldAttack)
                {
                    actions.Add(new AttackAction
                    {
                        AttackerIndex = atkIdx,
                        Target = target.type,
                        TargetIndex = target.index,
                    });
                }
            }

            return actions;
        }

        // ─── Smart Target Selection (forge-inspired combat math) ─
        private (TargetType type, int index, bool profitable, bool evenTrade) ChooseSmartTarget(
            DaemonInstance attacker, PlayerState opponent)
        {
            int bestScore = int.MinValue;
            int bestIdx = 0;
            bool bestProfitable = false;
            bool bestEven = false;

            // Taunt enforcement: if any opponent daemon has taunt, must target it
            var taunters = opponent.Field
                .Select((d, i) => (d, i))
                .Where(x => x.d.HasTaunt && !x.d.Stealthed)
                .ToList();
            bool hasTaunters = taunters.Count > 0;

            for (int i = 0; i < opponent.Field.Count; i++)
            {
                var defender = opponent.Field[i];

                // Skip stealthed targets (poketcg: can't target invisible)
                if (defender.Stealthed) continue;

                // Skip non-taunters when taunters exist
                if (hasTaunters && !defender.HasTaunt) continue;

                float elemMult = ElementSystem.GetElementMatchup(attacker.Card.element, defender.Card.element);
                float creatMult = ElementSystem.GetCreatureMatchup(attacker.Card.creatureType, defender.Card.creatureType);
                int effectiveDamage = (int)(attacker.Attack * elemMult * creatMult);

                // Account for damage reduction (poketcg: Defender attachment)
                effectiveDamage = Math.Max(0, effectiveDamage - defender.DamageReduction);

                bool weKill = effectiveDamage >= defender.CurrentAshe;
                // Account for burn miss chance (50% to miss)
                bool theyKill = defender.Attack >= attacker.CurrentAshe;
                if (defender.Burning) theyKill = false; // 50% miss — optimistic

                int defValue = EvaluateDaemon(defender);
                int atkValue = EvaluateDaemon(attacker);

                int tradeScore = 0;
                if (weKill && !theyKill) tradeScore = defValue + 100; // Clear win
                else if (weKill && theyKill) tradeScore = defValue - atkValue; // Trade
                else if (!weKill && !theyKill) tradeScore = effectiveDamage * 2 - 10; // Chip
                else tradeScore = -atkValue; // We die, they don't

                // Bonus for killing poisoned/burning targets (they'll die soon anyway — less value)
                if (defender.Poisoned || defender.Burning)
                    tradeScore -= 15;

                // Hard AI: bonus for element advantage
                if (_difficulty >= Difficulty.Hard && elemMult > 1f)
                    tradeScore += 30;

                if (tradeScore > bestScore)
                {
                    bestScore = tradeScore;
                    bestIdx = i;
                    bestProfitable = weKill && !theyKill;
                    bestEven = weKill && theyKill && defValue >= atkValue;
                }
            }

            // If no good daemon target, check if we should skip
            if (bestScore < -50 && _aggression < 3)
                return (TargetType.Daemon, bestIdx, false, false);

            return (TargetType.Daemon, bestIdx, bestProfitable, bestEven);
        }

        // ─── Helpers ────────────────────────────────────────
        private int FindBestMaskTarget(PlayerState player, MaskCardData mask)
        {
            int best = -1;
            int bestScore = -1;
            for (int i = 0; i < player.Field.Count; i++)
            {
                var d = player.Field[i];
                int score = d.Attack * 10 + d.CurrentAshe * 5;
                // Prefer daemons that can attack this turn
                if (d.CanAttack && !d.HasAttacked) score += 30;
                // Haste mask: prefer daemons with summoning sickness
                if (mask.effectType == MaskEffectType.Haste && !d.CanAttack) score += 50;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private int FindWeakestPillar(PlayerState opponent)
        {
            int best = -1;
            int lowestHp = int.MaxValue;
            for (int i = 0; i < opponent.Pillars.Count; i++)
            {
                if (opponent.Pillars[i].Destroyed) continue;
                if (opponent.Pillars[i].CurrentHp < lowestHp)
                {
                    lowestHp = opponent.Pillars[i].CurrentHp;
                    best = i;
                }
            }
            return best;
        }
    }
}
