// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Effect Resolver (Orchestrator)
//  The bridge between BattleManager and EffectFunctions.
//  BattleManager calls "something happened" →
//  Resolver finds matching EffectEntries → executes them.
// ═══════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DualCraft.Battle;
using DualCraft.Cards;
using DualCraft.Core;

namespace DualCraft.Effects
{
    public class EffectResolver
    {
        private readonly GameState _state;
        public event System.Action<string> OnEffectLog;

        /// <summary>
        /// If set to true after resolving a seal, the triggering action
        /// should be cancelled (e.g., NegateAction was used).
        /// </summary>
        public bool ActionNegated { get; private set; }

        public EffectResolver(GameState state)
        {
            _state = state;
        }

        // ─── Public API: called by BattleManager ─────────────

        /// <summary>Fire all OnSummon effects for a daemon just played.</summary>
        public void OnDaemonSummoned(int ownerIndex, DaemonInstance daemon)
        {
            if (daemon.Silenced) return;
            var entries = GetEffectsForDaemon(daemon, EffectTrigger.OnSummon);
            var ctx = MakeContext(ownerIndex, daemon);
            ExecuteAll(entries, ctx);

            // Check opponent seals with OnSummon trigger
            CheckSeals(1 - ownerIndex, SealTrigger.OnSummon, daemon);
        }

        /// <summary>Fire all OnDestroy effects for a daemon that just died.</summary>
        public void OnDaemonDestroyed(int ownerIndex, DaemonInstance daemon)
        {
            if (daemon.Silenced) return;
            var entries = GetEffectsForDaemon(daemon, EffectTrigger.OnDestroy);
            var ctx = MakeContext(ownerIndex, daemon);
            ExecuteAll(entries, ctx);

            // Check opponent seals with OnDaemonDestroy trigger
            CheckSeals(1 - ownerIndex, SealTrigger.OnDaemonDestroy, daemon);
        }

        /// <summary>Fire OnAttack effects before damage is dealt.</summary>
        public void OnDaemonAttacking(int ownerIndex, DaemonInstance attacker)
        {
            if (attacker.Silenced) return;
            var entries = GetEffectsForDaemon(attacker, EffectTrigger.OnAttack);
            var ctx = MakeContext(ownerIndex, attacker);
            ExecuteAll(entries, ctx);

            // Check opponent seals with OnAttack trigger
            ActionNegated = false;
            CheckSeals(1 - ownerIndex, SealTrigger.OnAttack, attacker);
        }

        /// <summary>Fire when a daemon takes damage (for reactive abilities).</summary>
        public void OnDaemonDamaged(int ownerIndex, DaemonInstance daemon, int damage)
        {
            if (daemon.Silenced) return;

            // Thorns: reflect damage to attacker
            // (handled in BattleManager after combat resolution)

            var entries = GetEffectsForDaemon(daemon, EffectTrigger.OnDamaged);
            var ctx = MakeContext(ownerIndex, daemon);
            ExecuteAll(entries, ctx);
        }

        /// <summary>Execute an activated pillar ability.</summary>
        public void ActivatePillarAbility(int ownerIndex, PillarInstance pillar,
            ActivatedAbilityData ability)
        {
            var entry = MapActivatedAbility(ability);
            if (entry == null) return;
            var ctx = MakeContext(ownerIndex);
            ctx.SourcePillar = pillar;
            ctx.SourceCard = pillar.Card;
            Execute(entry, ctx);
        }

        /// <summary>Apply passive pillar effects at turn start.</summary>
        public void ApplyPillarPassives(int ownerIndex)
        {
            var player = _state.Players[ownerIndex];
            foreach (var pillar in player.Pillars.Where(p => !p.Destroyed && p.Revealed))
            {
                var passive = pillar.Card.passiveEffect;
                if (passive == null || string.IsNullOrEmpty(passive.passiveType))
                    continue;

                var ctx = MakeContext(ownerIndex);
                ctx.SourcePillar = pillar;
                ctx.SourceCard = pillar.Card;

                ApplyPillarPassive(passive, ctx);
            }
        }

        /// <summary>Fire pillar OnDestroy effects.</summary>
        public void OnPillarDestroyed(int ownerIndex, PillarInstance pillar)
        {
            var destroy = pillar.Card.onDestroyedEffect;
            if (destroy == null || string.IsNullOrEmpty(destroy.destroyType))
                return;

            var ctx = MakeContext(ownerIndex);
            ctx.SourcePillar = pillar;
            ctx.SourceCard = pillar.Card;

            ApplyPillarDestroyEffect(destroy, ctx);
        }

        /// <summary>Apply the active domain's effect at turn start.</summary>
        public void ApplyDomainEffect(int currentPlayer)
        {
            if (_state.ActiveDomain == null) return;
            var domain = _state.ActiveDomain.Card;
            var ctx = MakeContext(_state.ActiveDomain.Owner);
            ctx.SourceCard = domain;

            var entry = MapDomainEffect(domain);
            if (entry != null)
                Execute(entry, ctx);
        }

        /// <summary>Apply mask effects. Called when mask is equipped or at turn start.</summary>
        public void ApplyMaskEffect(int ownerIndex, DaemonInstance daemon, MaskCardData mask)
        {
            var entry = MapMaskEffect(mask);
            if (entry == null) return;
            var ctx = MakeContext(ownerIndex, daemon);
            Execute(entry, ctx);
        }

        /// <summary>Process turn-start upkeep: passives, domain, status ticks.</summary>
        public void OnTurnStart(int currentPlayer)
        {
            ApplyPillarPassives(currentPlayer);
            ApplyDomainEffect(currentPlayer);
            TickStatusEffects(currentPlayer);
        }

        /// <summary>Process turn-end effects.</summary>
        public void OnTurnEnd(int currentPlayer)
        {
            var player = _state.Players[currentPlayer];

            // Fire OnTurnEnd abilities
            foreach (var daemon in player.Field.ToList())
            {
                if (daemon.Silenced) continue;
                var entries = GetEffectsForDaemon(daemon, EffectTrigger.OnTurnEnd);
                if (entries.Count > 0)
                {
                    var ctx = MakeContext(currentPlayer, daemon);
                    ExecuteAll(entries, ctx);
                }
            }
        }

        /// <summary>Clean up dead daemons from the field. Returns list of destroyed.</summary>
        public List<DaemonInstance> CleanupDead(int playerIndex)
        {
            var player = _state.Players[playerIndex];
            var dead = player.Field.Where(d => d.CurrentAshe <= 0).ToList();
            foreach (var d in dead)
            {
                player.Field.Remove(d);
                player.AshePile.Add(new CardInstance
                {
                    InstanceId = d.InstanceId,
                    Card = d.Card,
                });
            }
            return dead;
        }

        // ─── Status Effect Ticking ───────────────────────────

        private void TickStatusEffects(int playerIndex)
        {
            var player = _state.Players[playerIndex];
            foreach (var d in player.Field)
            {
                // Freeze
                if (d.Frozen)
                {
                    d.FrozenTurns--;
                    if (d.FrozenTurns <= 0) d.Frozen = false;
                }
                // Stealth
                if (d.Stealthed)
                {
                    d.StealthTurns--;
                    if (d.StealthTurns <= 0) d.Stealthed = false;
                }
                // Entangle
                if (d.Entangled)
                {
                    d.EntangledTurns--;
                    if (d.EntangledTurns <= 0) d.Entangled = false;
                }
                // Taunt
                if (d.HasTaunt)
                {
                    d.TauntTurns--;
                    if (d.TauntTurns <= 0) d.HasTaunt = false;
                }
            }
        }

        // ─── Seal Checking ───────────────────────────────────

        private void CheckSeals(int sealOwnerIndex, SealTrigger trigger,
            DaemonInstance triggerSource)
        {
            var player = _state.Players[sealOwnerIndex];
            var matchingSeals = player.SealZone
                .Where(s => s.Card.trigger == trigger)
                .ToList();

            foreach (var seal in matchingSeals)
            {
                var entry = MapSealEffect(seal.Card);
                if (entry == null) continue;

                var ctx = MakeContext(sealOwnerIndex);
                ctx.SourceSeal = seal;
                ctx.SourceCard = seal.Card;
                ctx.TargetDaemon = triggerSource;

                string log = Execute(entry, ctx);

                // Check if this was a NegateAction
                if (entry.functionId == EffectFunctionId.NegateAction)
                    ActionNegated = true;

                // Remove used seal
                player.SealZone.Remove(seal);
                Log($"Seal triggered: {seal.Card.cardName}!");
            }
        }

        // ─── Mapping legacy data → EffectEntry ──────────────

        private List<EffectEntry> GetEffectsForDaemon(DaemonInstance daemon, EffectTrigger trigger)
        {
            var results = new List<EffectEntry>();
            if (daemon.Card.ability == null) return results;
            var ab = daemon.Card.ability;

            // Map AbilityType to EffectTrigger
            EffectTrigger abTrigger = ab.type switch
            {
                AbilityType.OnSummon => EffectTrigger.OnSummon,
                AbilityType.OnDestroy => EffectTrigger.OnDestroy,
                AbilityType.Passive => EffectTrigger.Passive,
                _ => EffectTrigger.None,
            };

            if (abTrigger != trigger) return results;

            var entry = MapEffectKey(ab.effectKey);
            if (entry != null)
                results.Add(entry);

            return results;
        }

        /// <summary>
        /// Maps an effectKey string to an EffectEntry. This is the bridge
        /// between the old string-based system and the new function ID system.
        /// As cards are migrated to store EffectEntry directly, this mapping
        /// becomes unnecessary.
        /// </summary>
        private EffectEntry MapEffectKey(string effectKey)
        {
            if (string.IsNullOrEmpty(effectKey)) return null;

            // Parse effectKey format: "functionName" or "functionName:p1,p2,p3"
            string key = effectKey;
            int[] parameters = System.Array.Empty<int>();
            int colonIdx = effectKey.IndexOf(':');
            if (colonIdx >= 0)
            {
                key = effectKey.Substring(0, colonIdx);
                var paramStr = effectKey.Substring(colonIdx + 1);
                parameters = paramStr.Split(',')
                    .Select(s => int.TryParse(s.Trim(), out int v) ? v : 0)
                    .ToArray();
            }

            // Map known effect keys to function IDs
            var (funcId, target) = key.ToLowerInvariant() switch
            {
                "deal-damage" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons),
                "damage-all" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons),
                "damage-all-enemies" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons),
                "heal" => (EffectFunctionId.HealAshe, EffectTarget.Self),
                "heal-self" => (EffectFunctionId.HealAshe, EffectTarget.Self),
                "heal-conjuror" => (EffectFunctionId.HealConjuror, EffectTarget.FriendlyConjuror),
                "damage-conjuror" => (EffectFunctionId.DamageConjuror, EffectTarget.EnemyConjuror),
                "drain" => (EffectFunctionId.DrainAshe, EffectTarget.TargetEnemyDaemon),
                "buff-atk" or "atk-buff" => (EffectFunctionId.BuffAttack, EffectTarget.Self),
                "buff-atk-all" => (EffectFunctionId.BuffAttack, EffectTarget.AllFriendlyDaemons),
                "buff-ashe" => (EffectFunctionId.BuffAshe, EffectTarget.Self),
                "debuff-atk" => (EffectFunctionId.DebuffAttack, EffectTarget.TargetEnemyDaemon),
                "element-atk-buff" => (EffectFunctionId.BuffAttackConditional, EffectTarget.AllFriendlyDaemons),
                "creature-ashe-buff" => (EffectFunctionId.BuffAshe, EffectTarget.AllFriendlyDaemons),
                "freeze" or "freeze-random" => (EffectFunctionId.Freeze, EffectTarget.RandomEnemyDaemon),
                "freeze-target" => (EffectFunctionId.Freeze, EffectTarget.TargetEnemyDaemon),
                "stealth" => (EffectFunctionId.Stealth, EffectTarget.Self),
                "entangle" => (EffectFunctionId.Entangle, EffectTarget.TargetEnemyDaemon),
                "shield" => (EffectFunctionId.Shield, EffectTarget.Self),
                "thorns" => (EffectFunctionId.Thorns, EffectTarget.Self),
                "taunt" => (EffectFunctionId.Taunt, EffectTarget.Self),
                "draw" or "draw-cards" => (EffectFunctionId.DrawCards, EffectTarget.None),
                "gain-will" => (EffectFunctionId.GainWill, EffectTarget.None),
                "drain-will" => (EffectFunctionId.DrainWill, EffectTarget.None),
                "destroy" or "destroy-target" => (EffectFunctionId.DestroyTarget, EffectTarget.TargetEnemyDaemon),
                "bounce" or "return-to-hand" => (EffectFunctionId.ReturnToHand, EffectTarget.TargetDaemon),
                "silence" => (EffectFunctionId.Silence, EffectTarget.TargetEnemyDaemon),
                "summon-token" => (EffectFunctionId.SummonToken, EffectTarget.None),
                "restore-pillar" => (EffectFunctionId.RestorePillarHp, EffectTarget.AllFriendlyPillars),
                "gain-loyalty" => (EffectFunctionId.GainLoyalty, EffectTarget.None),
                _ => (EffectFunctionId.None, EffectTarget.None),
            };

            if (funcId == EffectFunctionId.None)
            {
                Debug.LogWarning($"[EffectResolver] Unknown effectKey: {effectKey}");
                return null;
            }

            return new EffectEntry
            {
                trigger = EffectTrigger.None, // set by caller
                target = target,
                functionId = funcId,
                parameters = parameters,
            };
        }

        private EffectEntry MapDomainEffect(DomainCardData domain)
        {
            return domain.effectType switch
            {
                DomainEffectType.AtkBuffAll => new EffectEntry
                {
                    functionId = EffectFunctionId.AtkBuffAll,
                    target = EffectTarget.AllDaemons,
                    parameters = new[] { domain.effectValue },
                },
                DomainEffectType.DamageAllEnd => new EffectEntry
                {
                    functionId = EffectFunctionId.DamageAllEnd,
                    target = EffectTarget.AllDaemons,
                    parameters = new[] { domain.effectValue },
                },
                DomainEffectType.ElementAtkBuff => new EffectEntry
                {
                    functionId = EffectFunctionId.ElementAtkBuff,
                    target = EffectTarget.AllFriendlyDaemons,
                    parameters = new[] { (int)domain.effectElement, domain.effectValue },
                },
                DomainEffectType.ExtraDraw => new EffectEntry
                {
                    functionId = EffectFunctionId.ExtraDraw,
                    target = EffectTarget.None,
                    parameters = new[] { domain.effectValue },
                },
                DomainEffectType.PillarHeal => new EffectEntry
                {
                    functionId = EffectFunctionId.PillarHealDomain,
                    target = EffectTarget.AllFriendlyPillars,
                    parameters = new[] { domain.effectValue },
                },
                DomainEffectType.PillarRestore => new EffectEntry
                {
                    functionId = EffectFunctionId.RestorePillarHp,
                    target = EffectTarget.AllFriendlyPillars,
                    parameters = new[] { domain.effectValue },
                },
                DomainEffectType.Protection => new EffectEntry
                {
                    functionId = EffectFunctionId.Shield,
                    target = EffectTarget.AllFriendlyDaemons,
                    parameters = new[] { domain.effectValue },
                },
                _ => null,
            };
        }

        private EffectEntry MapMaskEffect(MaskCardData mask)
        {
            return mask.effectType switch
            {
                MaskEffectType.AtkBoost => new EffectEntry
                {
                    functionId = EffectFunctionId.BuffAttack,
                    target = EffectTarget.Self,
                    parameters = new[] { mask.effectValue },
                },
                MaskEffectType.AsheBoost => new EffectEntry
                {
                    functionId = EffectFunctionId.BuffAshe,
                    target = EffectTarget.Self,
                    parameters = new[] { mask.effectValue },
                },
                MaskEffectType.Haste => new EffectEntry
                {
                    // Haste = can attack immediately (handled in BattleManager)
                    functionId = EffectFunctionId.None,
                    target = EffectTarget.Self,
                },
                MaskEffectType.Stealth => new EffectEntry
                {
                    functionId = EffectFunctionId.Stealth,
                    target = EffectTarget.Self,
                    parameters = new[] { mask.effectValue },
                },
                MaskEffectType.Thorns => new EffectEntry
                {
                    functionId = EffectFunctionId.Thorns,
                    target = EffectTarget.Self,
                    parameters = new[] { mask.effectValue },
                },
                MaskEffectType.Entangle => new EffectEntry
                {
                    // Entangle the target of the mask (enemy daemon)
                    functionId = EffectFunctionId.Entangle,
                    target = EffectTarget.TargetEnemyDaemon,
                    parameters = new[] { mask.effectValue },
                },
                _ => null,
            };
        }

        private EffectEntry MapSealEffect(SealCardData seal)
        {
            return seal.effectType switch
            {
                SealEffectType.Drain => new EffectEntry
                {
                    functionId = EffectFunctionId.SealDrain,
                    target = EffectTarget.AttackingDaemon,
                    parameters = new[] { seal.effectValue },
                },
                SealEffectType.Destroy => new EffectEntry
                {
                    functionId = EffectFunctionId.DestroyTarget,
                    target = EffectTarget.AttackingDaemon,
                },
                SealEffectType.Negate => new EffectEntry
                {
                    functionId = EffectFunctionId.NegateAction,
                    target = EffectTarget.None,
                },
                SealEffectType.CounterSpell => new EffectEntry
                {
                    functionId = EffectFunctionId.CounterAndDamage,
                    target = EffectTarget.AttackingDaemon,
                    parameters = new[] { seal.effectValue },
                },
                SealEffectType.HealConjuror => new EffectEntry
                {
                    functionId = EffectFunctionId.HealConjuror,
                    target = EffectTarget.FriendlyConjuror,
                    parameters = new[] { seal.effectValue },
                },
                _ => null,
            };
        }

        private EffectEntry MapActivatedAbility(ActivatedAbilityData ability)
        {
            return MapEffectKey(ability.effectKey);
        }

        private void ApplyPillarPassive(PillarPassiveData passive, EffectContext ctx)
        {
            switch (passive.passiveType?.ToLowerInvariant())
            {
                case "element-atk-buff":
                    EffectFunctions.Execute(EffectFunctionId.BuffAttackConditional, ctx,
                        new[] { passive.value, (int)passive.element });
                    break;
                case "creature-ashe-buff":
                    foreach (var d in ctx.Owner.Field
                        .Where(d => d.Card.creatureType == passive.creatureType))
                    {
                        d.MaxAshe += passive.value;
                        d.CurrentAshe += passive.value;
                    }
                    break;
                case "atk-buff-all":
                    EffectFunctions.Execute(EffectFunctionId.BuffAttack, ctx,
                        new[] { passive.value });
                    break;
                case "heal-conjuror":
                    EffectFunctions.Execute(EffectFunctionId.HealConjuror, ctx,
                        new[] { passive.value });
                    break;
                default:
                    if (!string.IsNullOrEmpty(passive.passiveType))
                        Debug.LogWarning($"[EffectResolver] Unknown passive: {passive.passiveType}");
                    break;
            }
        }

        private void ApplyPillarDestroyEffect(PillarDestroyData destroy, EffectContext ctx)
        {
            switch (destroy.destroyType?.ToLowerInvariant())
            {
                case "freeze-random":
                    EffectFunctions.Execute(EffectFunctionId.Freeze, ctx,
                        new[] { destroy.value > 0 ? destroy.value : 1 });
                    ctx.TargetDaemon = EffectFunctions.ResolveTargetsByType(
                        EffectTarget.RandomEnemyDaemon, ctx).FirstOrDefault();
                    if (ctx.TargetDaemon != null)
                        Log($"Pillar's dying curse: {ctx.TargetDaemon.Card.cardName} frozen!");
                    break;
                case "damage-all-enemies":
                    EffectFunctions.Execute(EffectFunctionId.DealDamage, ctx,
                        new[] { destroy.value });
                    // Apply to all enemies
                    foreach (var d in ctx.Opponent.Field)
                    {
                        d.CurrentAshe -= destroy.value;
                        if (d.CurrentAshe < 0) d.CurrentAshe = 0;
                    }
                    Log($"Pillar's dying curse: {destroy.value} damage to all enemies!");
                    break;
                case "heal-conjuror":
                    EffectFunctions.Execute(EffectFunctionId.HealConjuror, ctx,
                        new[] { destroy.value });
                    Log($"Pillar's dying blessing: heal Conjuror for {destroy.value}!");
                    break;
                case "draw-cards":
                    EffectFunctions.Execute(EffectFunctionId.DrawCards, ctx,
                        new[] { destroy.value > 0 ? destroy.value : 1 });
                    break;
                default:
                    if (!string.IsNullOrEmpty(destroy.destroyType))
                        Debug.LogWarning($"[EffectResolver] Unknown destroy: {destroy.destroyType}");
                    break;
            }
        }

        // ─── Execution helpers ───────────────────────────────

        private string Execute(EffectEntry entry, EffectContext ctx)
        {
            if (entry == null || entry.functionId == EffectFunctionId.None)
                return null;

            // Resolve targets based on entry.target
            if (ctx.TargetDaemon == null)
            {
                var resolved = EffectFunctions.ResolveTargetsByType(entry.target, ctx);
                if (resolved.Count > 0)
                    ctx.TargetDaemon = resolved[0];
            }

            string log = EffectFunctions.Execute(entry.functionId, ctx, entry.parameters);
            if (log != null)
                Log(log);
            return log;
        }

        private void ExecuteAll(List<EffectEntry> entries, EffectContext ctx)
        {
            foreach (var entry in entries)
                Execute(entry, ctx);
        }

        private EffectContext MakeContext(int ownerIndex, DaemonInstance daemon = null)
        {
            return new EffectContext
            {
                State = _state,
                OwnerIndex = ownerIndex,
                SourceDaemon = daemon,
                SourceCard = daemon?.Card,
            };
        }

        private void Log(string msg)
        {
            OnEffectLog?.Invoke(msg);
        }
    }
}
