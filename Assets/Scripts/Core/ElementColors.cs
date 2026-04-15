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
