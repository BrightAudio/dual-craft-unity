// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Element Data & Matchup System
// ═══════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

namespace DualCraft.Core
{
    [System.Serializable]
    public class ElementInfo
    {
        public string displayName;
        public Color color;
        public string symbol;
        [TextArea] public string description;
    }

    [System.Serializable]
    public class CreatureTypeInfo
    {
        public string displayName;
        public string icon;
        public string sphere;
        public Rarity baseRarity;
    }

    public static class ElementSystem
    {
        // Element advantage chart
        private static readonly Dictionary<Element, Element[]> Advantages = new()
        {
            { Element.Flame, new[] { Element.Ice, Element.Nature } },
            { Element.Ice, new[] { Element.Air, Element.Nature } },
            { Element.Air, new[] { Element.Earth } },
            { Element.Earth, new[] { Element.Water } },
            { Element.Water, new[] { Element.Flame } },
            { Element.Light, new[] { Element.Dark } },
            { Element.Dark, new[] { Element.Light } },
            { Element.Nature, new[] { Element.Water, Element.Earth } },
        };

        private static readonly Dictionary<Element, Element[]> Weaknesses = new()
        {
            { Element.Flame, new[] { Element.Water } },
            { Element.Ice, new[] { Element.Flame } },
            { Element.Air, new[] { Element.Ice } },
            { Element.Earth, new[] { Element.Air, Element.Nature } },
            { Element.Water, new[] { Element.Earth, Element.Nature } },
            { Element.Light, new[] { Element.Dark } },
            { Element.Dark, new[] { Element.Light } },
            { Element.Nature, new[] { Element.Flame, Element.Ice } },
        };

        private static readonly Dictionary<Element, Element> Allies = new()
        {
            { Element.Flame, Element.Air },
            { Element.Ice, Element.Water },
            { Element.Water, Element.Earth },
            { Element.Earth, Element.Ice },
            { Element.Air, Element.Flame },
            { Element.Light, Element.Water },
            { Element.Dark, Element.Flame },
            { Element.Nature, Element.Light },
        };

        // Creature type matchups
        private static readonly Dictionary<CreatureType, CreatureType[]> CreatureAdvantages = new()
        {
            { CreatureType.Spirit, new[] { CreatureType.Artificial } },
            { CreatureType.Artificial, new[] { CreatureType.Machine } },
            { CreatureType.Machine, new[] { CreatureType.Elemental } },
            { CreatureType.Elemental, new[] { CreatureType.Spirit } },
            { CreatureType.Undead, new CreatureType[0] },
        };

        private static readonly Dictionary<CreatureType, CreatureType[]> CreatureWeaknesses = new()
        {
            { CreatureType.Spirit, new[] { CreatureType.Elemental } },
            { CreatureType.Artificial, new[] { CreatureType.Spirit } },
            { CreatureType.Machine, new[] { CreatureType.Artificial } },
            { CreatureType.Elemental, new[] { CreatureType.Machine } },
            { CreatureType.Undead, new CreatureType[0] },
        };

        public static float GetElementMatchup(Element attacker, Element defender)
        {
            if (System.Array.Exists(Advantages.GetValueOrDefault(attacker, System.Array.Empty<Element>()), e => e == defender))
                return GameConstants.SuperEffectiveMult;
            if (System.Array.Exists(Weaknesses.GetValueOrDefault(attacker, System.Array.Empty<Element>()), e => e == defender))
                return GameConstants.WeakMult;
            return GameConstants.NeutralMult;
        }

        public static float GetCreatureMatchup(CreatureType attacker, CreatureType defender)
        {
            if (System.Array.Exists(CreatureAdvantages.GetValueOrDefault(attacker, System.Array.Empty<CreatureType>()), c => c == defender))
                return GameConstants.CreatureAdvantageMult;
            if (System.Array.Exists(CreatureWeaknesses.GetValueOrDefault(attacker, System.Array.Empty<CreatureType>()), c => c == defender))
                return GameConstants.CreatureDisadvantageMult;
            return GameConstants.NeutralMult;
        }

        public static Element GetAlly(Element element)
        {
            return Allies.GetValueOrDefault(element, element);
        }

        public static bool IsAdvantaged(Element attacker, Element defender)
        {
            return System.Array.Exists(Advantages.GetValueOrDefault(attacker, System.Array.Empty<Element>()), e => e == defender);
        }

        public static bool IsWeakTo(Element attacker, Element defender)
        {
            return System.Array.Exists(Weaknesses.GetValueOrDefault(attacker, System.Array.Empty<Element>()), e => e == defender);
        }

        // Default color mappings
        public static Color GetElementColor(Element element) => element switch
        {
            Element.Flame => new Color(0.976f, 0.451f, 0.086f),   // #F97316
            Element.Ice => new Color(0.376f, 0.647f, 0.98f),      // #60A5FA
            Element.Water => new Color(0.024f, 0.714f, 0.831f),   // #06B6D4
            Element.Earth => new Color(0.518f, 0.8f, 0.086f),     // #84CC16
            Element.Air => new Color(0.58f, 0.639f, 0.722f),      // #94A3B8
            Element.Light => new Color(0.984f, 0.749f, 0.141f),   // #FBBF24
            Element.Dark => new Color(0.659f, 0.333f, 0.969f),    // #A855F7
            Element.Nature => new Color(0.133f, 0.773f, 0.369f),  // #22C55E
            _ => Color.white,
        };

        public static Color GetRarityColor(Rarity rarity) => rarity switch
        {
            Rarity.Common => new Color(0.612f, 0.639f, 0.686f),    // #9CA3AF
            Rarity.Rare => new Color(0.376f, 0.647f, 0.98f),       // #60A5FA
            Rarity.Epic => new Color(0.659f, 0.333f, 0.969f),      // #A855F7
            Rarity.Legendary => new Color(0.984f, 0.749f, 0.141f), // #FBBF24
            _ => Color.white,
        };
    }
}
