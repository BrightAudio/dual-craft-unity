// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Deck Definition (ScriptableObject)
//  Based on Pokemon TCG deck structure:
//    name, types, cards[{id, name, count}]
// ═══════════════════════════════════════════════════════

using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "NewDeck", menuName = "Dual Craft/Deck Definition")]
    public class DeckData : ScriptableObject
    {
        public string deckName;
        public Element element;
        public CreatureType primaryCreatureType;
        [TextArea] public string description;
        public bool isStarter;

        [Header("Cards (40 main deck)")]
        public DeckEntry[] cards;

        [Header("Pillars (4)")]
        public DeckEntry[] pillars;

        public int TotalMainCards
        {
            get
            {
                int total = 0;
                if (cards != null)
                    foreach (var entry in cards) total += entry.count;
                return total;
            }
        }

        public int TotalPillars
        {
            get
            {
                int total = 0;
                if (pillars != null)
                    foreach (var entry in pillars) total += entry.count;
                return total;
            }
        }

        public bool IsValid => TotalMainCards == GameConstants.DeckSize && TotalPillars == GameConstants.PillarCount;
    }

    [System.Serializable]
    public class DeckEntry
    {
        public CardData card;
        public int count = 1;
    }
}
