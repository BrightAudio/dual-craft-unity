// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card ScriptableObject (Data-Driven)
//  Inspired by Pokemon TCG data structure:
//    id, name, supertype, subtypes, hp, types, attacks,
//    abilities, weaknesses, rarity, images, flavorText
// ═══════════════════════════════════════════════════════

using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "NewCard", menuName = "Dual Craft/Card Data")]
    public class CardData : ScriptableObject
    {
        [Header("Identity")]
        public string cardId;
        public string cardName;
        public CardCategory category;
        public Rarity rarity;

        [Header("Visuals")]
        public Sprite artwork;
        public Sprite fullArt;

        [Header("Text")]
        [TextArea(2, 4)] public string description;
        [TextArea(1, 3)] public string flavorText;

        [Header("Cost")]
        public int willCost = -1; // -1 = auto from rarity

        public int GetWillCost()
        {
            if (willCost >= 0) return willCost;
            if (category == CardCategory.Pillar) return 0;
            return rarity switch
            {
                Rarity.Common => category == CardCategory.Daemon ? 2 : 1,
                Rarity.Rare => category == CardCategory.Daemon ? 3 : 2,
                Rarity.Epic => category == CardCategory.Daemon ? 4 : 3,
                Rarity.Legendary => category == CardCategory.Daemon ? 5 : 4,
                _ => 2,
            };
        }
    }

}
