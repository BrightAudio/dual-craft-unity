// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Effect Functions (Reusable Logic)
//  Each function is a pure game-logic operation. The same
//  function serves many cards — only the parameters differ.
//  "Untap X Mana of Y Type" = one function, 10 cards.
// ═══════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DualCraft.Battle;
using DualCraft.Core;

namespace DualCraft.Effects
{
    /// <summary>
    /// Static library of every reusable effect function.
    /// Each takes (EffectContext ctx, int[] p) and returns a
    /// human-readable log string describing what happened.
    /// </summary>
    public static class EffectFunctions
    {
        public delegate string EffectFunc(EffectContext ctx, int[] p);

        // ─── Registry: functionId → delegate ──────────────────────
        private static readonly Dictionary<EffectFunctionId, EffectFunc> _funcs = new()
        {
            // Damage / Heal
            { EffectFunctionId.DealDamage, DealDamage },
            { EffectFunctionId.HealAshe, HealAshe },
            { EffectFunctionId.HealConjuror, HealConjuror },
            { EffectFunctionId.DamageConjuror, DamageConjuror },
            { EffectFunctionId.DrainAshe, DrainAshe },

            // Stat Buffs
            { EffectFunctionId.BuffAttack, BuffAttack },
            { EffectFunctionId.BuffAshe, BuffAshe },
            { EffectFunctionId.DebuffAttack, DebuffAttack },
            { EffectFunctionId.BuffAttackConditional, BuffAttackConditional },

            // Status Effects
            { EffectFunctionId.Freeze, ApplyFreeze },
            { EffectFunctionId.Stealth, ApplyStealth },
            { EffectFunctionId.Entangle, ApplyEntangle },
            { EffectFunctionId.Shield, ApplyShield },
            { EffectFunctionId.Thorns, ApplyThorns },
            { EffectFunctionId.Taunt, ApplyTaunt },

            // Resource
            { EffectFunctionId.DrawCards, DrawCards },
            { EffectFunctionId.GainWill, GainWill },
            { EffectFunctionId.DrainWill, DrainWill },
            { EffectFunctionId.ReduceCost, ReduceCost },
            { EffectFunctionId.RestoreWill, RestoreWill },

            // Board Control
            { EffectFunctionId.DestroyTarget, DestroyTarget },
            { EffectFunctionId.ReturnToHand, ReturnToHand },
            { EffectFunctionId.Silence, SilenceTarget },
            { EffectFunctionId.DestroyWeakest, DestroyWeakest },
            { EffectFunctionId.DestroyByElement, DestroyByElement },

            // Pillar
            { EffectFunctionId.RestorePillarHp, RestorePillarHp },
            { EffectFunctionId.GainLoyalty, GainLoyalty },
            { EffectFunctionId.PillarElementBuff, PillarElementBuff },

            // Domain
            { EffectFunctionId.AtkBuffAll, AtkBuffAll },
            { EffectFunctionId.DamageAllEnd, DamageAllEnd },
            { EffectFunctionId.ElementAtkBuff, ElementAtkBuff },
            { EffectFunctionId.ExtraDraw, ExtraDraw },
            { EffectFunctionId.PillarHealDomain, PillarHealDomain },

            // Seal
            { EffectFunctionId.NegateAction, NegateAction },
            { EffectFunctionId.CounterAndDamage, CounterAndDamage },
            { EffectFunctionId.SealDrain, SealDrain },

            // Summoning
            { EffectFunctionId.SummonToken, SummonToken },

            // Multi-effect
            { EffectFunctionId.DamageAndDraw, DamageAndDraw },
            { EffectFunctionId.BuffAndHeal, BuffAndHeal },
            { EffectFunctionId.DamageAllAndHealSelf, DamageAllAndHealSelf },
        };

        /// <summary>Look up and execute a function by its ID.</summary>
        public static string Execute(EffectFunctionId id, EffectContext ctx, int[] parameters)
        {
            if (id == EffectFunctionId.None)
                return null;
            if (_funcs.TryGetValue(id, out var func))
                return func(ctx, parameters ?? System.Array.Empty<int>());
            Debug.LogWarning($"[EffectFunctions] No handler for {id}");
            return null;
        }

        private static int P(int[] p, int index, int fallback = 0)
            => index < p.Length ? p[index] : fallback;

        // ═══════════════════════════════════════════════════════
        //  DAMAGE / HEAL
        // ═══════════════════════════════════════════════════════

        private static string DealDamage(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 2);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.CurrentAshe -= amount;
                if (d.CurrentAshe < 0) d.CurrentAshe = 0;
            }
            return $"Deals {amount} damage";
        }

        private static string HealAshe(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 2);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.CurrentAshe = Mathf.Min(d.CurrentAshe + amount, d.MaxAshe);
            }
            return $"Heals {amount} Ashe";
        }

        private static string HealConjuror(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 3);
            var conj = ctx.Owner.Conjuror;
            conj.Hp = Mathf.Min(conj.Hp + amount, conj.MaxHp);
            return $"Heals Conjuror for {amount}";
        }

        private static string DamageConjuror(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 3);
            ctx.Opponent.Conjuror.Hp -= amount;
            if (ctx.Opponent.Conjuror.Hp < 0) ctx.Opponent.Conjuror.Hp = 0;
            return $"Deals {amount} to enemy Conjuror";
        }

        private static string DrainAshe(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 2);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                int actual = Mathf.Min(amount, d.CurrentAshe);
                d.CurrentAshe -= actual;
                if (ctx.SourceDaemon != null)
                    ctx.SourceDaemon.CurrentAshe = Mathf.Min(
                        ctx.SourceDaemon.CurrentAshe + actual, ctx.SourceDaemon.MaxAshe);
            }
            return $"Drains {amount} Ashe";
        }

        // ═══════════════════════════════════════════════════════
        //  STAT BUFFS
        // ═══════════════════════════════════════════════════════

        private static string BuffAttack(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            foreach (var d in ResolveTargetDaemons(ctx))
                d.Attack += amount;
            return $"+{amount} ATK";
        }

        private static string BuffAshe(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.MaxAshe += amount;
                d.CurrentAshe += amount;
            }
            return $"+{amount} Ashe";
        }

        private static string DebuffAttack(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            foreach (var d in ResolveTargetDaemons(ctx))
                d.Attack = Mathf.Max(0, d.Attack - amount);
            return $"-{amount} ATK";
        }

        private static string BuffAttackConditional(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            Element reqElement = (Element)P(p, 1, 0);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                if (d.Card.element == reqElement)
                    d.Attack += amount;
            }
            return $"+{amount} ATK to {reqElement} daemons";
        }

        // ═══════════════════════════════════════════════════════
        //  STATUS EFFECTS
        // ═══════════════════════════════════════════════════════

        private static string ApplyFreeze(EffectContext ctx, int[] p)
        {
            int turns = P(p, 0, 1);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.Frozen = true;
                d.FrozenTurns = Mathf.Max(d.FrozenTurns, turns);
            }
            return $"Frozen for {turns} turn(s)";
        }

        private static string ApplyStealth(EffectContext ctx, int[] p)
        {
            int turns = P(p, 0, 2);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.Stealthed = true;
                d.StealthTurns = Mathf.Max(d.StealthTurns, turns);
            }
            return $"Stealth for {turns} turn(s)";
        }

        private static string ApplyEntangle(EffectContext ctx, int[] p)
        {
            int turns = P(p, 0, 1);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.Entangled = true;
                d.EntangledTurns = Mathf.Max(d.EntangledTurns, turns);
            }
            return $"Entangled for {turns} turn(s)";
        }

        private static string ApplyShield(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 2);
            foreach (var d in ResolveTargetDaemons(ctx))
                d.ShieldAmount += amount;
            return $"Shield {amount}";
        }

        private static string ApplyThorns(EffectContext ctx, int[] p)
        {
            int damage = P(p, 0, 1);
            foreach (var d in ResolveTargetDaemons(ctx))
                d.ThornsDamage = Mathf.Max(d.ThornsDamage, damage);
            return $"Thorns {damage}";
        }

        private static string ApplyTaunt(EffectContext ctx, int[] p)
        {
            int turns = P(p, 0, 2);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.HasTaunt = true;
                d.TauntTurns = Mathf.Max(d.TauntTurns, turns);
            }
            return $"Taunt for {turns} turn(s)";
        }

        // ═══════════════════════════════════════════════════════
        //  RESOURCE
        // ═══════════════════════════════════════════════════════

        private static string DrawCards(EffectContext ctx, int[] p)
        {
            int count = P(p, 0, 1);
            var player = ctx.Owner;
            for (int i = 0; i < count; i++)
            {
                if (player.Deck.Count == 0 || player.Hand.Count >= GameConstants.MaxHandSize)
                    break;
                var card = player.Deck[0];
                player.Deck.RemoveAt(0);
                player.Hand.Add(card);
            }
            return $"Draw {count} card(s)";
        }

        private static string GainWill(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            ctx.Owner.Will = Mathf.Min(ctx.Owner.Will + amount, GameConstants.MaxWill);
            return $"+{amount} Will";
        }

        private static string DrainWill(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            int actual = Mathf.Min(amount, ctx.Opponent.Will);
            ctx.Opponent.Will -= actual;
            ctx.Owner.Will = Mathf.Min(ctx.Owner.Will + actual, GameConstants.MaxWill);
            return $"Drain {amount} Will";
        }

        private static string ReduceCost(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            ctx.Owner.CostReduction += amount;
            return $"Next card costs {amount} less";
        }

        private static string RestoreWill(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            ctx.Owner.Will = Mathf.Min(ctx.Owner.Will + amount, ctx.Owner.MaxWill);
            return $"Restore {amount} Will";
        }

        // ═══════════════════════════════════════════════════════
        //  BOARD CONTROL
        // ═══════════════════════════════════════════════════════

        private static string DestroyTarget(EffectContext ctx, int[] p)
        {
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.CurrentAshe = 0;
            }
            return "Destroy target";
        }

        private static string ReturnToHand(EffectContext ctx, int[] p)
        {
            if (ctx.TargetDaemon != null)
            {
                var owner = ctx.TargetIsFriendly ? ctx.Owner : ctx.Opponent;
                int idx = owner.Field.IndexOf(ctx.TargetDaemon);
                if (idx >= 0)
                {
                    owner.Field.RemoveAt(idx);
                    owner.Hand.Add(new CardInstance
                    {
                        InstanceId = ctx.TargetDaemon.InstanceId,
                        Card = ctx.TargetDaemon.Card,
                    });
                }
            }
            return "Return to hand";
        }

        private static string SilenceTarget(EffectContext ctx, int[] p)
        {
            foreach (var d in ResolveTargetDaemons(ctx))
                d.Silenced = true;
            return "Silenced";
        }

        private static string DestroyWeakest(EffectContext ctx, int[] p)
        {
            int threshold = P(p, 0, 3);
            var targets = ctx.Opponent.Field.Where(d => d.CurrentAshe <= threshold).ToList();
            foreach (var d in targets)
                d.CurrentAshe = 0;
            return $"Destroy daemons with ≤{threshold} Ashe";
        }

        private static string DestroyByElement(EffectContext ctx, int[] p)
        {
            var elem = (Element)P(p, 0, 0);
            var targets = ctx.Opponent.Field.Where(d => d.Card.element == elem).ToList();
            foreach (var d in targets)
                d.CurrentAshe = 0;
            return $"Destroy all {elem} daemons";
        }

        // ═══════════════════════════════════════════════════════
        //  PILLAR
        // ═══════════════════════════════════════════════════════

        private static string RestorePillarHp(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 3);
            foreach (var pil in ctx.Owner.Pillars.Where(x => !x.Destroyed))
                pil.CurrentHp = Mathf.Min(pil.CurrentHp + amount, pil.MaxHp);
            return $"Restore {amount} HP to all Pillars";
        }

        private static string GainLoyalty(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            if (ctx.SourcePillar != null)
                ctx.SourcePillar.Loyalty += amount;
            return $"+{amount} Loyalty";
        }

        private static string PillarElementBuff(EffectContext ctx, int[] p)
        {
            var elem = (Element)P(p, 0, 0);
            int atkBuff = P(p, 1, 1);
            foreach (var d in ctx.Owner.Field.Where(d => d.Card.element == elem))
                d.Attack += atkBuff;
            return $"+{atkBuff} ATK to {elem} daemons";
        }

        // ═══════════════════════════════════════════════════════
        //  DOMAIN
        // ═══════════════════════════════════════════════════════

        private static string AtkBuffAll(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            foreach (var d in ctx.Owner.Field)
                d.Attack += amount;
            foreach (var d in ctx.Opponent.Field)
                d.Attack += amount;
            return $"+{amount} ATK to all daemons";
        }

        private static string DamageAllEnd(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 1);
            foreach (var d in ctx.Owner.Field.Concat(ctx.Opponent.Field))
            {
                d.CurrentAshe -= amount;
                if (d.CurrentAshe < 0) d.CurrentAshe = 0;
            }
            return $"{amount} damage to all daemons";
        }

        private static string ElementAtkBuff(EffectContext ctx, int[] p)
        {
            var elem = (Element)P(p, 0, 0);
            int amount = P(p, 1, 2);
            foreach (var d in ctx.Owner.Field.Where(d => d.Card.element == elem))
                d.Attack += amount;
            return $"+{amount} ATK to {elem} daemons";
        }

        private static string ExtraDraw(EffectContext ctx, int[] p)
        {
            int count = P(p, 0, 1);
            // This is applied at turn start by EffectResolver
            ctx.Owner.ExtraDraws += count;
            return $"+{count} extra draw(s) per turn";
        }

        private static string PillarHealDomain(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 2);
            foreach (var pil in ctx.Owner.Pillars.Where(x => !x.Destroyed))
                pil.CurrentHp = Mathf.Min(pil.CurrentHp + amount, pil.MaxHp);
            return $"Heal all Pillars for {amount}";
        }

        // ═══════════════════════════════════════════════════════
        //  SEAL
        // ═══════════════════════════════════════════════════════

        private static string NegateAction(EffectContext ctx, int[] p)
        {
            // The EffectResolver checks this and blocks the triggering action
            return "Action negated!";
        }

        private static string CounterAndDamage(EffectContext ctx, int[] p)
        {
            int damage = P(p, 0, 3);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.CurrentAshe -= damage;
                if (d.CurrentAshe < 0) d.CurrentAshe = 0;
            }
            return $"Counter! {damage} damage";
        }

        private static string SealDrain(EffectContext ctx, int[] p)
        {
            int amount = P(p, 0, 2);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                int actual = Mathf.Min(amount, d.CurrentAshe);
                d.CurrentAshe -= actual;
                ctx.Owner.Conjuror.Hp = Mathf.Min(
                    ctx.Owner.Conjuror.Hp + actual, ctx.Owner.Conjuror.MaxHp);
            }
            return $"Drain {amount} from attacker, heal Conjuror";
        }

        // ═══════════════════════════════════════════════════════
        //  SUMMONING
        // ═══════════════════════════════════════════════════════

        private static string SummonToken(EffectContext ctx, int[] p)
        {
            int atk = P(p, 0, 1);
            int ashe = P(p, 1, 1);
            int count = P(p, 2, 1);
            for (int i = 0; i < count; i++)
            {
                if (ctx.Owner.Field.Count >= GameConstants.MaxFieldDaemons)
                    break;
                ctx.Owner.Field.Add(new DaemonInstance
                {
                    InstanceId = System.Guid.NewGuid().ToString(),
                    Card = ctx.SourceDaemon?.Card,
                    CurrentAshe = ashe,
                    MaxAshe = ashe,
                    Attack = atk,
                    AsheCost = 0,
                    CanAttack = false,
                    HasAttacked = false,
                });
            }
            return $"Summon {count}x {atk}/{ashe} token(s)";
        }

        // ═══════════════════════════════════════════════════════
        //  MULTI-EFFECT
        // ═══════════════════════════════════════════════════════

        private static string DamageAndDraw(EffectContext ctx, int[] p)
        {
            int damage = P(p, 0, 2);
            int draw = P(p, 1, 1);
            foreach (var d in ResolveTargetDaemons(ctx))
            {
                d.CurrentAshe -= damage;
                if (d.CurrentAshe < 0) d.CurrentAshe = 0;
            }
            DrawCards(ctx, new[] { draw });
            return $"{damage} damage, draw {draw}";
        }

        private static string BuffAndHeal(EffectContext ctx, int[] p)
        {
            int atkBuff = P(p, 0, 1);
            int heal = P(p, 1, 2);
            if (ctx.SourceDaemon != null)
            {
                ctx.SourceDaemon.Attack += atkBuff;
                ctx.SourceDaemon.CurrentAshe = Mathf.Min(
                    ctx.SourceDaemon.CurrentAshe + heal, ctx.SourceDaemon.MaxAshe);
            }
            return $"+{atkBuff} ATK, heal {heal}";
        }

        private static string DamageAllAndHealSelf(EffectContext ctx, int[] p)
        {
            int damage = P(p, 0, 2);
            int heal = P(p, 1, 3);
            foreach (var d in ctx.Opponent.Field)
            {
                d.CurrentAshe -= damage;
                if (d.CurrentAshe < 0) d.CurrentAshe = 0;
            }
            if (ctx.SourceDaemon != null)
                ctx.SourceDaemon.CurrentAshe = Mathf.Min(
                    ctx.SourceDaemon.CurrentAshe + heal, ctx.SourceDaemon.MaxAshe);
            return $"{damage} to all enemies, heal self {heal}";
        }

        // ═══════════════════════════════════════════════════════
        //  TARGET RESOLUTION
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Resolves the target enum to a concrete list of DaemonInstances.
        /// Uses the EffectContext's EffectEntry target (set by EffectResolver).
        /// </summary>
        private static List<DaemonInstance> ResolveTargetDaemons(EffectContext ctx)
        {
            // If a specific TargetDaemon was set (player-chosen), use it
            if (ctx.TargetDaemon != null)
                return new List<DaemonInstance> { ctx.TargetDaemon };

            // Otherwise return self
            if (ctx.SourceDaemon != null)
                return new List<DaemonInstance> { ctx.SourceDaemon };

            return new List<DaemonInstance>();
        }

        /// <summary>
        /// Resolve targets based on the EffectTarget enum — called by EffectResolver
        /// before passing to the execute function.
        /// </summary>
        public static List<DaemonInstance> ResolveTargetsByType(
            EffectTarget targetType, EffectContext ctx)
        {
            return targetType switch
            {
                EffectTarget.Self => ctx.SourceDaemon != null
                    ? new List<DaemonInstance> { ctx.SourceDaemon }
                    : new List<DaemonInstance>(),
                EffectTarget.AllFriendlyDaemons => new List<DaemonInstance>(ctx.Owner.Field),
                EffectTarget.AllEnemyDaemons => new List<DaemonInstance>(ctx.Opponent.Field),
                EffectTarget.AllDaemons => ctx.Owner.Field.Concat(ctx.Opponent.Field).ToList(),
                EffectTarget.RandomEnemyDaemon => PickRandom(ctx.Opponent.Field),
                EffectTarget.RandomFriendlyDaemon => PickRandom(ctx.Owner.Field),
                EffectTarget.StrongestEnemy => PickStrongest(ctx.Opponent.Field),
                EffectTarget.WeakestEnemy => PickWeakest(ctx.Opponent.Field),
                EffectTarget.TargetDaemon or EffectTarget.TargetEnemyDaemon
                    or EffectTarget.TargetFriendlyDaemon =>
                    ctx.TargetDaemon != null
                        ? new List<DaemonInstance> { ctx.TargetDaemon }
                        : new List<DaemonInstance>(),
                EffectTarget.AttackingDaemon => ctx.TargetDaemon != null
                    ? new List<DaemonInstance> { ctx.TargetDaemon }
                    : new List<DaemonInstance>(),
                _ => new List<DaemonInstance>(),
            };
        }

        private static List<DaemonInstance> PickRandom(List<DaemonInstance> pool)
        {
            if (pool.Count == 0) return new List<DaemonInstance>();
            return new List<DaemonInstance> { pool[Random.Range(0, pool.Count)] };
        }

        private static List<DaemonInstance> PickStrongest(List<DaemonInstance> pool)
        {
            if (pool.Count == 0) return new List<DaemonInstance>();
            return new List<DaemonInstance> { pool.OrderByDescending(d => d.Attack).First() };
        }

        private static List<DaemonInstance> PickWeakest(List<DaemonInstance> pool)
        {
            if (pool.Count == 0) return new List<DaemonInstance>();
            return new List<DaemonInstance> { pool.OrderBy(d => d.Attack).First() };
        }
    }
}
