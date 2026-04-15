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

        // ── Creature type matchups ──────────────────────
        // Elemental  → strong vs Spirit (raw power overwhelms ethereal)
        // Spirit     → strong vs Undead (purifies the corrupted)
        // Undead     → strong vs Machine (corrosion/decay rusts gears)
        //             + strong vs Artificial (entropy unravels constructs)
        // Machine    → strong vs Elemental (steel contains chaos)
        // Artificial → strong vs Spirit (synthetic wards disrupt ethereal)
        //
        // Undead is the wildcard: hits two types hard but also takes
        // extra from two, making it high-risk/high-reward.
        private static readonly Dictionary<CreatureType, CreatureType[]> CreatureAdvantages = new()
        {
            { CreatureType.Elemental, new[] { CreatureType.Spirit } },
            { CreatureType.Spirit, new[] { CreatureType.Undead } },
            { CreatureType.Undead, new[] { CreatureType.Machine, CreatureType.Artificial } },
            { CreatureType.Machine, new[] { CreatureType.Elemental } },
            { CreatureType.Artificial, new[] { CreatureType.Spirit } },
        };

        // Creature type weaknesses (attacker → defenders it is weak against)
        private static readonly Dictionary<CreatureType, CreatureType[]> CreatureWeaknesses = new()
        {
            { CreatureType.Elemental, new[] { CreatureType.Machine } },
            { CreatureType.Spirit, new[] { CreatureType.Elemental, CreatureType.Artificial } },
            { CreatureType.Undead, new[] { CreatureType.Spirit } },
            { CreatureType.Machine, new[] { CreatureType.Undead } },
            { CreatureType.Artificial, new[] { CreatureType.Undead } },
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