// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Element Colors (Visual Layer Only)
//  UI color lookups separated from game logic so
//  ElementSystem stays Unity-free.
// ═══════════════════════════════════════════════════════
using UnityEngine;

namespace DualCraft.Core
{
    public static class ElementColors
    {
        /// <summary>Returns a colour for the given element, used in UI.</summary>
        public static Color GetElementColor(Element element) => element switch
        {
            Element.Flame => new Color(0.976f, 0.451f, 0.086f),
            Element.Ice => new Color(0.376f, 0.647f, 0.98f),
            Element.Water => new Color(0.024f, 0.714f, 0.831f),
            Element.Earth => new Color(0.518f, 0.8f, 0.086f),
            Element.Air => new Color(0.58f, 0.639f, 0.722f),
            Element.Light => new Color(0.984f, 0.749f, 0.141f),
            Element.Dark => new Color(0.659f, 0.333f, 0.969f),
            Element.Nature => new Color(0.133f, 0.773f, 0.369f),
            _ => Color.white,
        };

        /// <summary>Returns a colour for the given creature type, used in UI.</summary>
        public static Color GetCreatureTypeColor(CreatureType ct) => ct switch
        {
            CreatureType.Elemental  => new Color(0.976f, 0.451f, 0.086f),  // fiery orange
            CreatureType.Spirit     => new Color(0.529f, 0.808f, 0.980f),  // spectral blue
            CreatureType.Undead     => new Color(0.545f, 0.271f, 0.675f),  // necrotic purple
            CreatureType.Machine    => new Color(0.600f, 0.600f, 0.600f),  // gunmetal
            CreatureType.Artificial => new Color(0.255f, 0.878f, 0.816f),  // cyan-teal
            _ => Color.white,
        };

        /// <summary>Returns a colour for the given rarity, used in UI.</summary>
        public static Color GetRarityColor(Rarity rarity) => rarity switch
        {
            Rarity.Common => new Color(0.612f, 0.639f, 0.686f),
            Rarity.Rare => new Color(0.376f, 0.647f, 0.98f),
            Rarity.Epic => new Color(0.659f, 0.333f, 0.969f),
            Rarity.Legendary => new Color(0.984f, 0.749f, 0.141f),
            _ => Color.white,
        };
    }
}
