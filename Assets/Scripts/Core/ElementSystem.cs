// ═══════════════════════════════════════════════════════
// DUAL CRAFT — Element Data & Matchup System
// Pure game logic: element/creature matchup tables and
// damage multiplier lookups. Zero Unity dependencies.
// UI colors are in ElementColors.cs.
// ═══════════════════════════════════════════════════════
using System.Collections.Generic;

namespace DualCraft.Core
{
    public static class ElementSystem
    {
        // Element advantage chart (attacker → defenders it is strong against)
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

        // Element weakness chart (attacker → defenders it is weak against)
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

        // Creature type matchups (attacker → defenders it is strong against)
        private static readonly Dictionary<CreatureType, CreatureType[]> CreatureAdvantages = new()
        {
            { CreatureType.Spirit, new[] { CreatureType.Artificial } },
            { CreatureType.Artificial, new[] { CreatureType.Machine } },
            { CreatureType.Machine, new[] { CreatureType.Elemental } },
            { CreatureType.Elemental, new[] { CreatureType.Spirit } },
            { CreatureType.Undead, new CreatureType[0] },
        };

        // Creature type weaknesses (attacker → defenders it is weak against)
        private static readonly Dictionary<CreatureType, CreatureType[]> CreatureWeaknesses = new()
        {
            { CreatureType.Spirit, new[] { CreatureType.Elemental } },
            { CreatureType.Artificial, new[] { CreatureType.Spirit } },
            { CreatureType.Machine, new[] { CreatureType.Artificial } },
            { CreatureType.Elemental, new[] { CreatureType.Machine } },
            { CreatureType.Undead, new CreatureType[0] },
        };

        /// <summary>
        /// Returns a damage multiplier based on the attacking and defending elements.
        /// Super effective hits use GameConstants.SuperEffectiveMult, weak hits use
        /// GameConstants.WeakMult, and neutral hits use GameConstants.NeutralMult.
        /// </summary>
        public static float GetElementMatchup(Element attacker, Element defender)
        {
            if (System.Array.Exists(Advantages.GetValueOrDefault(attacker, System.Array.Empty<Element>()), e => e == defender))
                return GameConstants.SuperEffectiveMult;
            if (System.Array.Exists(Weaknesses.GetValueOrDefault(attacker, System.Array.Empty<Element>()), e => e == defender))
                return GameConstants.WeakMult;
            return GameConstants.NeutralMult;
        }

        /// <summary>
        /// Returns a damage multiplier for creature type matchups.  Advantage uses
        /// GameConstants.CreatureAdvantageMult, disadvantage uses
        /// GameConstants.CreatureDisadvantageMult, otherwise neutral.
        /// </summary>
        public static float GetCreatureMatchup(CreatureType attacker, CreatureType defender)
        {
            if (System.Array.Exists(CreatureAdvantages.GetValueOrDefault(attacker, System.Array.Empty<CreatureType>()), c => c == defender))
                return GameConstants.CreatureAdvantageMult;
            if (System.Array.Exists(CreatureWeaknesses.GetValueOrDefault(attacker, System.Array.Empty<CreatureType>()), c => c == defender))
                return GameConstants.CreatureDisadvantageMult;
            return GameConstants.NeutralMult;
        }
    }
}