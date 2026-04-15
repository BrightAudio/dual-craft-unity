// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card Loader (JSON-First, Cloud-Ready)
//  "Put the card data into the cloud and grab them at
//   the start of the game. Change card data from anywhere."
//  — Reddit Post 1
//
//  Priority order:
//    1. Remote JSON (URL) — for live balance patches
//    2. Local all_cards.json — offline / fallback
//    3. ScriptableObject CardDatabase — legacy fallback
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DualCraft.Cards;
using DualCraft.Core;
using DualCraft.Effects;

namespace DualCraft.Data
{
    /// <summary>
    /// Runtime card registry that loads all card data from JSON.
    /// Cards are plain data objects — no ScriptableObjects needed.
    /// Adding a new card = adding a new row to the JSON.
    /// </summary>
    public class CardLoader
    {
        private static CardLoader _instance;
        public static CardLoader Instance => _instance ??= new CardLoader();

        private Dictionary<string, RuntimeCard> _cards = new();
        private Dictionary<string, DeckTemplate> _decks = new();
        private bool _loaded;

        public bool IsLoaded => _loaded;
        public int CardCount => _cards.Count;

        /// <summary>
        /// Load all cards from the bundled JSON file.
        /// Call this once at game startup.
        /// </summary>
        public void LoadFromResources(string path = "CardData/all_cards")
        {
            var textAsset = Resources.Load<TextAsset>(path);
            if (textAsset == null)
            {
                Debug.LogError($"[CardLoader] Could not load {path}");
                return;
            }
            LoadFromJson(textAsset.text);
        }

        /// <summary>
        /// Load from raw JSON string — used for remote/cloud loading.
        /// </summary>
        public void LoadFromJson(string json)
        {
            var db = CardDatabaseJson.FromJson(json);
            if (db == null || db.cards == null)
            {
                Debug.LogError("[CardLoader] Failed to parse JSON");
                return;
            }

            _cards.Clear();
            _decks.Clear();

            foreach (var entry in db.cards)
            {
                var card = ParseCard(entry);
                if (card != null)
                    _cards[card.Id] = card;
            }

            if (db.decks != null)
            {
                foreach (var deck in db.decks)
                    _decks[deck.name] = ParseDeck(deck);
            }

            _loaded = true;
            Debug.Log($"[CardLoader] Loaded {_cards.Count} cards, {_decks.Count} decks from JSON");
        }

        /// <summary>
        /// Fallback: populate from existing ScriptableObject CardDatabase
        /// if JSON is unavailable (editor workflow / legacy).
        /// </summary>
        public void LoadFromScriptableObjects(CardDatabase soDb)
        {
            if (soDb == null) return;
            soDb.Initialize();
            foreach (var card in soDb.GetAllCards())
            {
                var runtime = ConvertFromSO(card);
                if (runtime != null)
                    _cards[runtime.Id] = runtime;
            }
            _loaded = true;
            Debug.Log($"[CardLoader] Loaded {_cards.Count} cards from ScriptableObjects");
        }

        // ─── Public API ──────────────────────────────────────

        public RuntimeCard GetCard(string id)
        {
            return _cards.GetValueOrDefault(id);
        }

        public IReadOnlyList<RuntimeCard> GetAllCards()
        {
            return _cards.Values.ToList();
        }

        public IReadOnlyList<RuntimeCard> GetCardsByCategory(CardCategory category)
        {
            return _cards.Values.Where(c => c.Category == category).ToList();
        }

        public IReadOnlyList<RuntimeCard> GetCardsByElement(Element element)
        {
            return _cards.Values.Where(c => c.Element == element).ToList();
        }

        public IReadOnlyList<RuntimeCard> GetCardsByRarity(Rarity rarity)
        {
            return _cards.Values.Where(c => c.Rarity == rarity).ToList();
        }

        public DeckTemplate GetDeck(string name)
        {
            return _decks.GetValueOrDefault(name);
        }

        public IReadOnlyList<DeckTemplate> GetAllDecks()
        {
            return _decks.Values.ToList();
        }

        public IReadOnlyList<DeckTemplate> GetStarterDecks()
        {
            return _decks.Values.Where(d => d.IsStarter).ToList();
        }

        // ─── Parsing ─────────────────────────────────────────

        private RuntimeCard ParseCard(CardJsonEntry e)
        {
            var card = new RuntimeCard
            {
                Id = e.id,
                Name = e.name,
                Category = ParseCategory(e.category),
                Rarity = ParseRarity(e.rarity),
                Description = e.description ?? "",
                FlavorText = e.flavorText ?? "",
                WillCost = e.willCost,
                ArtPath = $"CardArt/{e.id}",

                // Daemon stats
                Element = ParseElement(e.element),
                CreatureType = ParseCreatureType(e.creatureType),
                Ashe = e.ashe,
                Attack = e.attack,
                AsheCost = e.asheCost,
                EvolvesTo = e.evolvesTo ?? "",
                EvolutionCost = e.evolutionCost,

                // Pillar stats
                Hp = e.hp,
                Loyalty = e.loyalty,
                PassiveAbilityText = e.passiveAbility ?? "",

                // Mask stats
                Duration = e.duration,

                // Seal trigger
                SealTrigger = ParseSealTrigger(e.trigger),

                // Dispel target
                DispelTarget = ParseDispelTarget(e.target),
            };

            // ─── Parse effects (the function columns) ────────
            var effects = new List<EffectEntry>();

            // New-style: effects[] array with functionId + params
            if (e.effects != null)
            {
                foreach (var fx in e.effects)
                    effects.Add(fx.ToEffectEntry());
            }

            // Legacy: daemon ability with effectKey string
            if (e.ability != null && !string.IsNullOrEmpty(e.ability.effect))
            {
                var legacyEntry = MapLegacyAbility(e.ability);
                if (legacyEntry != null && !effects.Any(x => x.functionId == legacyEntry.functionId))
                    effects.Add(legacyEntry);
            }

            // Legacy: pillar passiveEffect
            if (e.passiveEffect != null && !string.IsNullOrEmpty(e.passiveEffect.type))
            {
                var pe = MapLegacyPillarPassive(e.passiveEffect);
                if (pe != null) effects.Add(pe);
            }

            // Legacy: pillar onDestroyedEffect
            if (e.onDestroyedEffect != null && !string.IsNullOrEmpty(e.onDestroyedEffect.type))
            {
                var de = MapLegacyPillarDestroy(e.onDestroyedEffect);
                if (de != null) effects.Add(de);
            }

            // Legacy: pillar activatedAbilities
            if (e.activatedAbilities != null)
            {
                foreach (var ab in e.activatedAbilities)
                {
                    var ae = MapLegacyActivated(ab);
                    if (ae != null) effects.Add(ae);
                }
            }

            // Legacy: domain effect
            if (e.effect != null && !string.IsNullOrEmpty(e.effect.type))
            {
                var de = MapLegacyDomainEffect(e.effect);
                if (de != null) effects.Add(de);
            }

            // Legacy: seal effect (stored in effect field)
            if (e.category == "seal" && e.effect != null)
            {
                var se = MapLegacySealEffect(e.effect, e.trigger);
                if (se != null) effects.Add(se);
            }

            // Legacy: mask effect
            if (e.category == "mask" && e.effect != null)
            {
                var me = MapLegacyMaskEffect(e.effect);
                if (me != null) effects.Add(me);
            }

            // Legacy: dispel counterEffect
            if (e.counterEffect != null && !string.IsNullOrEmpty(e.counterEffect.type))
            {
                var ce = MapLegacyCounterEffect(e.counterEffect);
                if (ce != null) effects.Add(ce);
            }

            // Legacy: conjuror abilities
            if (e.abilities != null)
            {
                foreach (var ab in e.abilities)
                {
                    var cje = MapLegacyActivated(new ActivatedAbilityJsonEntry
                    {
                        name = ab.name,
                        description = ab.description,
                        loyaltyCost = ab.loyaltyCost,
                        effect = ab.effect,
                    });
                    if (cje != null) effects.Add(cje);
                }
            }

            card.Effects = effects.ToArray();
            return card;
        }

        private DeckTemplate ParseDeck(DeckJsonEntry d)
        {
            return new DeckTemplate
            {
                Name = d.name,
                Element = ParseElement(d.element),
                CreatureType = ParseCreatureType(d.creatureType),
                Description = d.description ?? "",
                IsStarter = d.isStarter,
                CardIds = d.cards ?? Array.Empty<string>(),
                PillarIds = d.pillars ?? Array.Empty<string>(),
            };
        }

        // ─── Legacy effect mapping ──────────────────────────

        private EffectEntry MapLegacyAbility(AbilityJsonEntry ab)
        {
            var trigger = ab.type?.ToLowerInvariant() switch
            {
                "on-summon" or "onsummon" => EffectTrigger.OnSummon,
                "on-destroy" or "ondestroy" => EffectTrigger.OnDestroy,
                "passive" => EffectTrigger.Passive,
                _ => EffectTrigger.None,
            };

            var (funcId, target, parms) = MapEffectString(ab.effect);
            return funcId == EffectFunctionId.None ? null : new EffectEntry
            {
                trigger = trigger,
                target = target,
                functionId = funcId,
                parameters = parms,
                description = ab.description ?? "",
            };
        }

        private EffectEntry MapLegacyPillarPassive(EffectJsonEntry e)
        {
            var (funcId, target, parms) = e.type?.ToLowerInvariant() switch
            {
                "start-turn-damage" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons, new[] { e.value }),
                "element-atk-buff" => (EffectFunctionId.BuffAttackConditional, EffectTarget.AllFriendlyDaemons,
                    new[] { e.value, (int)ParseElement(e.element) }),
                "creature-ashe-buff" => (EffectFunctionId.BuffAshe, EffectTarget.AllFriendlyDaemons, new[] { e.value }),
                "atk-buff-all" => (EffectFunctionId.BuffAttack, EffectTarget.AllFriendlyDaemons, new[] { e.value }),
                "heal-conjuror" => (EffectFunctionId.HealConjuror, EffectTarget.FriendlyConjuror, new[] { e.value }),
                "gain-will" => (EffectFunctionId.GainWill, EffectTarget.None, new[] { e.value }),
                "draw-cards" => (EffectFunctionId.DrawCards, EffectTarget.None, new[] { e.value }),
                _ => (EffectFunctionId.None, EffectTarget.None, Array.Empty<int>()),
            };
            return funcId == EffectFunctionId.None ? null : new EffectEntry
            {
                trigger = EffectTrigger.OnTurnStart,
                target = target,
                functionId = funcId,
                parameters = parms,
                description = "",
            };
        }

        private EffectEntry MapLegacyPillarDestroy(EffectJsonEntry e)
        {
            var (funcId, target, parms) = e.type?.ToLowerInvariant() switch
            {
                "damage-all-enemies" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons, new[] { e.value }),
                "freeze-random" => (EffectFunctionId.Freeze, EffectTarget.RandomEnemyDaemon, new[] { e.value > 0 ? e.value : 1 }),
                "heal-conjuror" => (EffectFunctionId.HealConjuror, EffectTarget.FriendlyConjuror, new[] { e.value }),
                "draw-cards" => (EffectFunctionId.DrawCards, EffectTarget.None, new[] { e.value }),
                "restore-pillar" => (EffectFunctionId.RestorePillarHp, EffectTarget.AllFriendlyPillars, new[] { e.value }),
                _ => (EffectFunctionId.None, EffectTarget.None, Array.Empty<int>()),
            };
            return funcId == EffectFunctionId.None ? null : new EffectEntry
            {
                trigger = EffectTrigger.OnDestroy,
                target = target,
                functionId = funcId,
                parameters = parms,
                description = "",
            };
        }

        private EffectEntry MapLegacyActivated(ActivatedAbilityJsonEntry ab)
        {
            if (string.IsNullOrEmpty(ab.effect)) return null;
            var (funcId, target, parms) = MapEffectString(ab.effect);
            return funcId == EffectFunctionId.None ? null : new EffectEntry
            {
                trigger = EffectTrigger.Activated,
                target = target,
                functionId = funcId,
                parameters = parms,
                description = ab.description ?? "",
            };
        }

        private EffectEntry MapLegacyDomainEffect(EffectJsonEntry e)
        {
            var (funcId, target, parms) = e.type?.ToLowerInvariant() switch
            {
                "atk-buff-all" => (EffectFunctionId.AtkBuffAll, EffectTarget.AllDaemons, new[] { e.value }),
                "damage-all-end" => (EffectFunctionId.DamageAllEnd, EffectTarget.AllDaemons, new[] { e.value }),
                "element-atk-buff" => (EffectFunctionId.ElementAtkBuff, EffectTarget.AllFriendlyDaemons,
                    new[] { (int)ParseElement(e.element), e.value }),
                "extra-draw" => (EffectFunctionId.ExtraDraw, EffectTarget.None, new[] { e.value }),
                "pillar-heal" => (EffectFunctionId.PillarHealDomain, EffectTarget.AllFriendlyPillars, new[] { e.value }),
                "pillar-restore" => (EffectFunctionId.RestorePillarHp, EffectTarget.AllFriendlyPillars, new[] { e.value }),
                "protection" => (EffectFunctionId.Shield, EffectTarget.AllFriendlyDaemons, new[] { e.value }),
                _ => (EffectFunctionId.None, EffectTarget.None, Array.Empty<int>()),
            };
            return funcId == EffectFunctionId.None ? null : new EffectEntry
            {
                trigger = EffectTrigger.OnPlay,
                target = target,
                functionId = funcId,
                parameters = parms,
                description = "",
            };
        }

        private EffectEntry MapLegacySealEffect(EffectJsonEntry e, string triggerStr)
        {
            var sealTrigger = ParseSealTrigger(triggerStr) switch
            {
                SealTrigger.OnAttack => EffectTrigger.OnSealTriggered,
                SealTrigger.OnSummon => EffectTrigger.OnSealTriggered,
                SealTrigger.OnDaemonDestroy => EffectTrigger.OnSealTriggered,
                SealTrigger.OnSpell => EffectTrigger.OnSealTriggered,
                _ => EffectTrigger.OnSealTriggered,
            };
            var (funcId, target, parms) = e.type?.ToLowerInvariant() switch
            {
                "drain" => (EffectFunctionId.SealDrain, EffectTarget.AttackingDaemon, new[] { e.value }),
                "destroy" => (EffectFunctionId.DestroyTarget, EffectTarget.AttackingDaemon, Array.Empty<int>()),
                "negate" => (EffectFunctionId.NegateAction, EffectTarget.None, Array.Empty<int>()),
                "counter-spell" => (EffectFunctionId.CounterAndDamage, EffectTarget.AttackingDaemon, new[] { e.value }),
                "heal-conjuror" => (EffectFunctionId.HealConjuror, EffectTarget.FriendlyConjuror, new[] { e.value }),
                _ => (EffectFunctionId.None, EffectTarget.None, Array.Empty<int>()),
            };
            return funcId == EffectFunctionId.None ? null : new EffectEntry
            {
                trigger = sealTrigger,
                target = target,
                functionId = funcId,
                parameters = parms,
                description = "",
            };
        }

        private EffectEntry MapLegacyMaskEffect(EffectJsonEntry e)
        {
            var (funcId, target, parms) = e.type?.ToLowerInvariant() switch
            {
                "atk-boost" => (EffectFunctionId.BuffAttack, EffectTarget.Self, new[] { e.value }),
                "ashe-boost" => (EffectFunctionId.BuffAshe, EffectTarget.Self, new[] { e.value }),
                "haste" => (EffectFunctionId.None, EffectTarget.Self, Array.Empty<int>()), // handled specially
                "stealth" => (EffectFunctionId.Stealth, EffectTarget.Self, new[] { e.value }),
                "thorns" => (EffectFunctionId.Thorns, EffectTarget.Self, new[] { e.value }),
                "entangle" => (EffectFunctionId.Entangle, EffectTarget.TargetEnemyDaemon, new[] { e.value }),
                _ => (EffectFunctionId.None, EffectTarget.None, Array.Empty<int>()),
            };
            return funcId == EffectFunctionId.None ? null : new EffectEntry
            {
                trigger = EffectTrigger.OnMaskEquipped,
                target = target,
                functionId = funcId,
                parameters = parms,
                description = "",
            };
        }

        private EffectEntry MapLegacyCounterEffect(EffectJsonEntry e)
        {
            var (funcId, target, parms) = e.type?.ToLowerInvariant() switch
            {
                "damage-owner" => (EffectFunctionId.DamageConjuror, EffectTarget.EnemyConjuror, new[] { e.value }),
                "draw-cards" => (EffectFunctionId.DrawCards, EffectTarget.None, new[] { e.value }),
                "heal-conjuror" => (EffectFunctionId.HealConjuror, EffectTarget.FriendlyConjuror, new[] { e.value }),
                _ => (EffectFunctionId.None, EffectTarget.None, Array.Empty<int>()),
            };
            return funcId == EffectFunctionId.None ? null : new EffectEntry
            {
                trigger = EffectTrigger.OnPlay,
                target = target,
                functionId = funcId,
                parameters = parms,
                description = "",
            };
        }

        /// <summary>Maps legacy string effect keys to function IDs.</summary>
        private (EffectFunctionId, EffectTarget, int[]) MapEffectString(string effectKey)
        {
            if (string.IsNullOrEmpty(effectKey))
                return (EffectFunctionId.None, EffectTarget.None, Array.Empty<int>());

            // Common daemon ability effect keys
            return effectKey.ToLowerInvariant() switch
            {
                "blaze-trail" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons, new[] { 1 }),
                "immolate" => (EffectFunctionId.DamageConjuror, EffectTarget.EnemyConjuror, new[] { 3 }),
                "cinder-cloud" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons, new[] { 2 }),
                "reforge" => (EffectFunctionId.BuffAttack, EffectTarget.Self, new[] { 1 }),
                "frost-bite" => (EffectFunctionId.Freeze, EffectTarget.RandomEnemyDaemon, new[] { 1 }),
                "flash-freeze" => (EffectFunctionId.Freeze, EffectTarget.AllEnemyDaemons, new[] { 1 }),
                "glacial-armor" => (EffectFunctionId.Shield, EffectTarget.Self, new[] { 3 }),
                "avalanche" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons, new[] { 3 }),
                "tidal-wave" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons, new[] { 2 }),
                "heal-spring" or "spring-heal" => (EffectFunctionId.HealAshe, EffectTarget.AllFriendlyDaemons, new[] { 2 }),
                "whirlpool" => (EffectFunctionId.ReturnToHand, EffectTarget.RandomEnemyDaemon, new[] { 0 }),
                "undertow" => (EffectFunctionId.DebuffAttack, EffectTarget.AllEnemyDaemons, new[] { 1 }),
                "earthquake" => (EffectFunctionId.DealDamage, EffectTarget.AllDaemons, new[] { 2 }),
                "stone-wall" => (EffectFunctionId.Shield, EffectTarget.Self, new[] { 4 }),
                "tremor" => (EffectFunctionId.DealDamage, EffectTarget.RandomEnemyDaemon, new[] { 3 }),
                "gale-force" => (EffectFunctionId.ReturnToHand, EffectTarget.TargetEnemyDaemon, new[] { 0 }),
                "tailwind" => (EffectFunctionId.BuffAttack, EffectTarget.AllFriendlyDaemons, new[] { 1 }),
                "zephyr-dash" => (EffectFunctionId.Stealth, EffectTarget.Self, new[] { 1 }),
                "holy-light" => (EffectFunctionId.HealConjuror, EffectTarget.FriendlyConjuror, new[] { 3 }),
                "radiance" => (EffectFunctionId.HealAshe, EffectTarget.AllFriendlyDaemons, new[] { 2 }),
                "purify" => (EffectFunctionId.Silence, EffectTarget.TargetEnemyDaemon, new[] { 0 }),
                "shadow-strike" => (EffectFunctionId.DealDamage, EffectTarget.WeakestEnemy, new[] { 4 }),
                "life-drain" or "drain" => (EffectFunctionId.DrainAshe, EffectTarget.TargetEnemyDaemon, new[] { 2 }),
                "devour" => (EffectFunctionId.DestroyWeakest, EffectTarget.AllEnemyDaemons, new[] { 3 }),
                "overgrowth" => (EffectFunctionId.BuffAshe, EffectTarget.AllFriendlyDaemons, new[] { 2 }),
                "entangle" => (EffectFunctionId.Entangle, EffectTarget.TargetEnemyDaemon, new[] { 2 }),
                "photosynthesis" => (EffectFunctionId.HealAshe, EffectTarget.Self, new[] { 3 }),
                "summon-token" or "spawn" => (EffectFunctionId.SummonToken, EffectTarget.None, new[] { 1, 1, 2 }),
                // Conjuror abilities
                "cj-flame-buff" => (EffectFunctionId.BuffAttackConditional, EffectTarget.AllFriendlyDaemons, new[] { 1, (int)Element.Flame }),
                "cj-flame-burn" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons, new[] { 2 }),
                "cj-ice-freeze" => (EffectFunctionId.Freeze, EffectTarget.RandomEnemyDaemon, new[] { 1 }),
                "cj-ice-shield" => (EffectFunctionId.Shield, EffectTarget.AllFriendlyDaemons, new[] { 2 }),
                "cj-water-heal" => (EffectFunctionId.HealAshe, EffectTarget.AllFriendlyDaemons, new[] { 2 }),
                "cj-water-draw" => (EffectFunctionId.DrawCards, EffectTarget.None, new[] { 2 }),
                "cj-earth-wall" => (EffectFunctionId.Shield, EffectTarget.AllFriendlyDaemons, new[] { 3 }),
                "cj-earth-quake" => (EffectFunctionId.DealDamage, EffectTarget.AllEnemyDaemons, new[] { 2 }),
                "cj-air-gale" => (EffectFunctionId.ReturnToHand, EffectTarget.TargetEnemyDaemon, new[] { 0 }),
                "cj-air-tailwind" => (EffectFunctionId.BuffAttack, EffectTarget.AllFriendlyDaemons, new[] { 1 }),
                "cj-light-heal" => (EffectFunctionId.HealConjuror, EffectTarget.FriendlyConjuror, new[] { 5 }),
                "cj-light-purify" => (EffectFunctionId.Silence, EffectTarget.TargetEnemyDaemon, new[] { 0 }),
                "cj-dark-drain" => (EffectFunctionId.DrainAshe, EffectTarget.StrongestEnemy, new[] { 3 }),
                "cj-dark-devour" => (EffectFunctionId.DestroyWeakest, EffectTarget.AllEnemyDaemons, new[] { 3 }),
                "cj-nature-grow" => (EffectFunctionId.BuffAshe, EffectTarget.AllFriendlyDaemons, new[] { 2 }),
                "cj-nature-entangle" => (EffectFunctionId.Entangle, EffectTarget.StrongestEnemy, new[] { 2 }),
                // Pillar activated abilities
                "pillar-damage" => (EffectFunctionId.DealDamage, EffectTarget.TargetEnemyDaemon, new[] { 3 }),
                "pillar-heal" => (EffectFunctionId.HealAshe, EffectTarget.TargetFriendlyDaemon, new[] { 3 }),
                "pillar-buff" => (EffectFunctionId.BuffAttack, EffectTarget.AllFriendlyDaemons, new[] { 1 }),
                "pillar-freeze" => (EffectFunctionId.Freeze, EffectTarget.TargetEnemyDaemon, new[] { 1 }),
                "pillar-draw" => (EffectFunctionId.DrawCards, EffectTarget.None, new[] { 1 }),
                "pillar-shield" => (EffectFunctionId.Shield, EffectTarget.AllFriendlyDaemons, new[] { 2 }),
                "pillar-will" => (EffectFunctionId.GainWill, EffectTarget.None, new[] { 1 }),
                _ => (EffectFunctionId.None, EffectTarget.None, Array.Empty<int>()),
            };
        }

        // ─── ScriptableObject conversion (legacy) ───────────

        private RuntimeCard ConvertFromSO(CardData card)
        {
            var runtime = new RuntimeCard
            {
                Id = card.cardId,
                Name = card.cardName,
                Category = card.category,
                Rarity = card.rarity,
                Description = card.description ?? "",
                FlavorText = card.flavorText ?? "",
                WillCost = card.willCost,
                ArtPath = $"CardArt/{card.cardId}",
            };

            if (card is DaemonCardData d)
            {
                runtime.Element = d.element;
                runtime.CreatureType = d.creatureType;
                runtime.Ashe = d.ashe;
                runtime.Attack = d.attack;
                runtime.AsheCost = d.asheCost;
                runtime.EvolvesTo = d.evolvesTo != null ? d.evolvesTo.cardId : "";
                runtime.EvolutionCost = d.evolutionCost;
            }
            else if (card is PillarCardData p)
            {
                runtime.Element = p.element;
                runtime.Hp = p.hp;
                runtime.Loyalty = p.loyalty;
            }
            else if (card is ConjurorCardData cj)
            {
                runtime.Element = cj.element;
                runtime.Loyalty = cj.loyalty;
            }
            else if (card is DomainCardData dom)
            {
                runtime.Element = dom.effectElement;
            }

            runtime.Effects = Array.Empty<EffectEntry>();
            return runtime;
        }

        // ─── Enum parsing ────────────────────────────────────

        private static CardCategory ParseCategory(string s) => s?.ToLowerInvariant() switch
        {
            "daemon" => CardCategory.Daemon,
            "pillar" => CardCategory.Pillar,
            "domain" => CardCategory.Domain,
            "mask" => CardCategory.Mask,
            "seal" => CardCategory.Seal,
            "dispel" => CardCategory.Dispel,
            "conjuror" => CardCategory.Conjuror,
            _ => CardCategory.Daemon,
        };

        private static Rarity ParseRarity(string s) => s?.ToLowerInvariant() switch
        {
            "common" => Rarity.Common,
            "rare" => Rarity.Rare,
            "epic" => Rarity.Epic,
            "legendary" => Rarity.Legendary,
            _ => Rarity.Common,
        };

        private static Element ParseElement(string s) => s?.ToLowerInvariant() switch
        {
            "flame" or "fire" => Element.Flame,
            "ice" or "frost" => Element.Ice,
            "water" => Element.Water,
            "earth" => Element.Earth,
            "air" or "wind" => Element.Air,
            "light" => Element.Light,
            "dark" or "shadow" => Element.Dark,
            "nature" or "plant" => Element.Nature,
            _ => Element.Flame,
        };

        private static CreatureType ParseCreatureType(string s) => s?.ToLowerInvariant() switch
        {
            "elemental" => CreatureType.Elemental,
            "machine" => CreatureType.Machine,
            "artificial" => CreatureType.Artificial,
            "spirit" => CreatureType.Spirit,
            "undead" => CreatureType.Undead,
            _ => CreatureType.Elemental,
        };

        private static SealTrigger ParseSealTrigger(string s) => s?.ToLowerInvariant() switch
        {
            "on-attack" or "onattack" => SealTrigger.OnAttack,
            "on-summon" or "onsummon" => SealTrigger.OnSummon,
            "on-daemon-destroy" or "ondaemondestroy" => SealTrigger.OnDaemonDestroy,
            "on-spell" or "onspell" => SealTrigger.OnSpell,
            _ => SealTrigger.OnAttack,
        };

        private static DispelTarget ParseDispelTarget(string s) => s?.ToLowerInvariant() switch
        {
            "domain" => DispelTarget.Domain,
            "mask" => DispelTarget.Mask,
            "seal" => DispelTarget.Seal,
            "any" => DispelTarget.Any,
            _ => DispelTarget.Any,
        };
    }

    // ═══════════════════════════════════════════════════════
    //  Runtime Card — Pure data, no ScriptableObject
    //  This is the "row" from the database. Every stat,
    //  property, and effect is a column.
    // ═══════════════════════════════════════════════════════

    [Serializable]
    public class RuntimeCard
    {
        // ─── Identity columns ────────────────────────────
        public string Id;
        public string Name;
        public CardCategory Category;
        public Rarity Rarity;
        public string Description;
        public string FlavorText;
        public int WillCost;
        public string ArtPath;

        // ─── Daemon stat columns ─────────────────────────
        public Element Element;
        public CreatureType CreatureType;
        public int Ashe;
        public int Attack;
        public int AsheCost;
        public string EvolvesTo;
        public int EvolutionCost;

        // ─── Pillar stat columns ─────────────────────────
        public int Hp;
        public int Loyalty;
        public string PassiveAbilityText;

        // ─── Mask column ─────────────────────────────────
        public int Duration;

        // ─── Seal column ─────────────────────────────────
        public SealTrigger SealTrigger;

        // ─── Dispel column ───────────────────────────────
        public DispelTarget DispelTarget;

        // ─── Effect function columns ─────────────────────
        // Each entry = one effect row with functionId + params.
        // "5 columns after the function column reserved for parameters"
        public EffectEntry[] Effects;

        /// <summary>Get computed will cost (auto from rarity if -1).</summary>
        public int GetWillCost()
        {
            if (WillCost >= 0) return WillCost;
            if (Category == CardCategory.Pillar) return 0;
            return Rarity switch
            {
                Rarity.Common => Category == CardCategory.Daemon ? 2 : 1,
                Rarity.Rare => Category == CardCategory.Daemon ? 3 : 2,
                Rarity.Epic => Category == CardCategory.Daemon ? 4 : 3,
                Rarity.Legendary => Category == CardCategory.Daemon ? 5 : 4,
                _ => 2,
            };
        }
    }

    [Serializable]
    public class DeckTemplate
    {
        public string Name;
        public Element Element;
        public CreatureType CreatureType;
        public string Description;
        public bool IsStarter;
        public string[] CardIds;
        public string[] PillarIds;
    }
}
