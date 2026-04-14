// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card Data JSON Format
//  Matches the web version's cards.ts export structure
// ═══════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace DualCraft.Data
{
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

    // Unified card entry — all card types in one structure
    [Serializable]
    public class CardJsonEntry
    {
        // Common fields
        public string id;
        public string name;
        public string category; // daemon, pillar, domain, mask, seal, dispel, conjuror
        public string rarity;   // common, rare, epic, legendary
        public string description;
        public string flavorText;

        // Daemon fields
        public string element;
        public string creatureType;
        public int ashe;
        public int attack;
        public int asheCost;
        public string evolvesTo;
        public int evolutionCost;
        public AbilityJsonEntry ability;

        // Pillar fields
        public int hp;
        public int loyalty;
        public string passiveAbility;
        public EffectJsonEntry passiveEffect;
        public EffectJsonEntry onDestroyedEffect;
        public ActivatedAbilityJsonEntry[] activatedAbilities;

        // Domain fields
        public EffectJsonEntry effect;

        // Mask fields
        public int duration;

        // Seal fields
        public string trigger; // on-attack, on-spell, on-daemon-destroy, on-summon

        // Dispel fields
        public string target; // domain, mask, seal, any
        public EffectJsonEntry counterEffect;

        // Conjuror fields
        public ConjurorAbilityJsonEntry[] abilities;
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
