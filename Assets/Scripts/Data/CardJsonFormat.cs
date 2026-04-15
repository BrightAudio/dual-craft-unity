// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card Data JSON Format
//  Matches the web version's cards.ts export structure
// ═══════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace DualCraft.Data
{
    using Effects;

    // Root wrapper — matches all_cards.json
    [Serializable]
    public class CardDatabaseJson
    {
        public string version;
        public string generatedFrom;
        public int totalCards;
        public CardJsonEntry[] cards;
        public DeckJsonEntry[] decks;

        public static CardDatabaseJson FromJson(string json)
        {
            return JsonUtility.FromJson<CardDatabaseJson>(json);
        }
    }

    // Unified card entry — all card types in one row
    // Every possible stat/effect/property is a column (Reddit post 1)
    [Serializable]
    public class CardJsonEntry
    {
        // ─── Identity columns ────────────────────────────
        public string id;
        public string name;
        public string category;
        public string rarity;
        public string description;
        public string flavorText;
        public int willCost = -1;

        // ─── Stat columns (daemon) ───────────────────────
        public string element;
        public string creatureType;
        public int ashe;
        public int attack;
        public int asheCost;
        public string evolvesTo;
        public int evolutionCost;

        // ─── Pillar stat columns ─────────────────────────
        public int hp;
        public int loyalty;
        public string passiveAbility;

        // ─── Mask stat columns ───────────────────────────
        public int duration;

        // ─── Seal stat columns ───────────────────────────
        public string trigger;

        // ─── Dispel stat columns ─────────────────────────
        public string target;

        // ─── Legacy effect data (backwards compat) ───────
        public AbilityJsonEntry ability;
        public EffectJsonEntry passiveEffect;
        public EffectJsonEntry onDestroyedEffect;
        public ActivatedAbilityJsonEntry[] activatedAbilities;
        public EffectJsonEntry effect;
        public EffectJsonEntry counterEffect;
        public ConjurorAbilityJsonEntry[] abilities;

        // ─── New: function-based effects (Post 2) ────────
        // Each card can have multiple effects. Each effect is a row with:
        //   trigger, target, functionId, and up to 5 int parameters.
        // "Untap X Mana of Y Type" = one function, change X and Y.
        public EffectJsonData[] effects;
    }

    // ─── Function-based effect data (the "function column + 5 param columns") ───
    [Serializable]
    public class EffectJsonData
    {
        public string trigger;     // maps to EffectTrigger enum
        public string target;      // maps to EffectTarget enum
        public int functionId;     // maps to EffectFunctionId enum
        public int p0;             // parameter 0
        public int p1;             // parameter 1
        public int p2;             // parameter 2
        public int p3;             // parameter 3
        public int p4;             // parameter 4
        public string description; // human-readable tooltip

        public int[] GetParams()
        {
            // Return only non-zero trailing params to keep it compact
            if (p4 != 0) return new[] { p0, p1, p2, p3, p4 };
            if (p3 != 0) return new[] { p0, p1, p2, p3 };
            if (p2 != 0) return new[] { p0, p1, p2 };
            if (p1 != 0) return new[] { p0, p1 };
            if (p0 != 0) return new[] { p0 };
            return System.Array.Empty<int>();
        }

        public EffectEntry ToEffectEntry()
        {
            return new EffectEntry
            {
                trigger = ParseTrigger(trigger),
                target = ParseTarget(target),
                functionId = (EffectFunctionId)functionId,
                parameters = GetParams(),
                description = description,
            };
        }

        private static EffectTrigger ParseTrigger(string s)
        {
            if (string.IsNullOrEmpty(s)) return EffectTrigger.None;
            return s.ToLowerInvariant().Replace("-", "").Replace("_", "") switch
            {
                "onsummon" => EffectTrigger.OnSummon,
                "ondestroy" => EffectTrigger.OnDestroy,
                "onattack" => EffectTrigger.OnAttack,
                "ondamaged" => EffectTrigger.OnDamaged,
                "passive" => EffectTrigger.Passive,
                "activated" => EffectTrigger.Activated,
                "onturnstart" => EffectTrigger.OnTurnStart,
                "onturnend" => EffectTrigger.OnTurnEnd,
                "ondraw" => EffectTrigger.OnDraw,
                "onplay" => EffectTrigger.OnPlay,
                "onsealtriggered" => EffectTrigger.OnSealTriggered,
                "onpillarreveal" => EffectTrigger.OnPillarReveal,
                "onevolve" => EffectTrigger.OnEvolve,
                "onmaskequipped" => EffectTrigger.OnMaskEquipped,
                "onmaskexpired" => EffectTrigger.OnMaskExpired,
                _ => EffectTrigger.None,
            };
        }

        private static EffectTarget ParseTarget(string s)
        {
            if (string.IsNullOrEmpty(s)) return EffectTarget.None;
            return s.ToLowerInvariant().Replace("-", "").Replace("_", "") switch
            {
                "self" => EffectTarget.Self,
                "allfriendlydaemons" => EffectTarget.AllFriendlyDaemons,
                "allenemydaemons" => EffectTarget.AllEnemyDaemons,
                "alldaemons" => EffectTarget.AllDaemons,
                "randomenemydaemon" => EffectTarget.RandomEnemyDaemon,
                "randomfriendlydaemon" => EffectTarget.RandomFriendlyDaemon,
                "friendlyconjuror" => EffectTarget.FriendlyConjuror,
                "enemyconjuror" => EffectTarget.EnemyConjuror,
                "targetdaemon" => EffectTarget.TargetDaemon,
                "targetenemydaemon" => EffectTarget.TargetEnemyDaemon,
                "targetfriendlydaemon" => EffectTarget.TargetFriendlyDaemon,
                "allfriendlypillars" => EffectTarget.AllFriendlyPillars,
                "allenemypillars" => EffectTarget.AllEnemyPillars,
                "strongestenemy" => EffectTarget.StrongestEnemy,
                "weakestenemy" => EffectTarget.WeakestEnemy,
                "allplayers" => EffectTarget.AllPlayers,
                "attackingdaemon" => EffectTarget.AttackingDaemon,
                "triggersource" => EffectTarget.TriggerSource,
                _ => EffectTarget.None,
            };
        }
    }

    [Serializable]
    public class AbilityJsonEntry
    {
        public string name;
        public string description;
        public string type;   // passive, on-summon, on-destroy
        public string effect;  // effect key string
    }

    [Serializable]
    public class EffectJsonEntry
    {
        public string type;
        public int value;
        public string element;
        public string creatureType;
        public int interval;
        public int ashe;
        public int attack;
    }

    [Serializable]
    public class ActivatedAbilityJsonEntry
    {
        public string name;
        public string description;
        public int loyaltyCost;
        public string effect;
    }

    [Serializable]
    public class ConjurorAbilityJsonEntry
    {
        public string name;
        public string description;
        public int loyaltyCost;
        public string effect;
    }

    [Serializable]
    public class DeckJsonEntry
    {
        public string name;
        public string element;
        public string creatureType;
        public string description;
        public bool isStarter;
        public string[] cards;   // flat array of card IDs (with duplicates for counts)
        public string[] pillars; // flat array of pillar IDs
    }
}
