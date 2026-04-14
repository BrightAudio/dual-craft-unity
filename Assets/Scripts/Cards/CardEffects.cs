// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card Effect Data (Serializable structs)
// ═══════════════════════════════════════════════════════

using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [System.Serializable]
    public class AbilityData
    {
        public string abilityName;
        [TextArea] public string description;
        public AbilityType type;
        public string effectKey;
    }

    [System.Serializable]
    public class PillarPassiveData
    {
        public string passiveType; // e.g. "element-atk-buff", "creature-ashe-buff"
        public Element element;
        public CreatureType creatureType;
        public int value;
    }

    [System.Serializable]
    public class PillarDestroyData
    {
        public string destroyType; // e.g. "freeze-random", "damage-all-enemies"
        public int value;
        public Element element;
    }

    [System.Serializable]
    public class ActivatedAbilityData
    {
        public string abilityName;
        [TextArea] public string description;
        public int loyaltyCost;
        public string effectKey;
    }

    [System.Serializable]
    public class ConjurorAbilityData
    {
        public string abilityName;
        [TextArea] public string description;
        public int loyaltyCost;
        public string effectKey;
    }

    [System.Serializable]
    public class DispelCounterEffectData
    {
        public string effectType; // "damage-owner", "draw-cards", "heal-conjuror", etc.
        public int value;
    }

    [System.Serializable]
    public class DomainEffectData
    {
        public DomainEffectType type;
        public int value;
        public Element element;
    }
}
