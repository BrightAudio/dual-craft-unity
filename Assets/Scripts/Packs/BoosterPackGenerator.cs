using System;
using System.Collections.Generic;
using System.Linq;
using DualCraft.Cards;
using DualCraft.Core;

namespace DualCraft.Packs
{
    /// <summary>
    /// Generates booster packs for the game by selecting cards from a pool
    /// according to rarity.  The default distribution approximates common
    /// trading card games: most cards in a pack are common, with fewer rare
    /// or higher‑rarity cards.
    /// </summary>
    public class BoosterPackGenerator
    {
        private readonly IReadOnlyList<CardData> _cardPool;
        private readonly Random _rng = new Random();
        private readonly int _packSize;
        private readonly (int commons, int rares, int epics, int legendaries) _distribution;

        /// <summary>
        /// Constructs a booster pack generator using the supplied card pool.
        /// Optionally specify the pack size and rarity distribution.  If
        /// distribution is left at its default value, a standard distribution
        /// of 7 commons, 2 rares and 1 epic is used.
        /// </summary>
        public BoosterPackGenerator(IEnumerable<CardData> cardPool,
            int packSize = 10,
            (int commons, int rares, int epics, int legendaries) distribution = default)
        {
            _cardPool = cardPool.ToList();
            _packSize = packSize;
            if (distribution.Equals(default((int, int, int, int))))
            {
                _distribution = (commons: 7, rares: 2, epics: 1, legendaries: 0);
            }
            else
            {
                _distribution = distribution;
            }
        }

        /// <summary>
        /// Opens a booster pack, returning a list of card data objects.  The
        /// generator respects the configured rarity distribution and falls
        /// back to lower rarities if necessary.  The pack size may exceed
        /// the sum of distribution counts; any remaining slots will be
        /// filled with commons.
        /// </summary>
        public List<CardData> OpenPack()
        {
            var pack = new List<CardData>();
            AddCardsByRarity(pack, Rarity.Legendary, _distribution.legendaries);
            AddCardsByRarity(pack, Rarity.Epic, _distribution.epics);
            AddCardsByRarity(pack, Rarity.Rare, _distribution.rares);
            AddCardsByRarity(pack, Rarity.Common, _distribution.commons);
            // Fill remaining slots with commons
            while (pack.Count < _packSize)
            {
                var commonPool = _cardPool.Where(c => c.rarity == Rarity.Common).ToList();
                if (commonPool.Count == 0)
                    break;
                pack.Add(commonPool[_rng.Next(commonPool.Count)]);
            }
            return pack;
        }

        private void AddCardsByRarity(List<CardData> pack, Rarity rarity, int count)
        {
            if (count <= 0)
                return;
            var pool = _cardPool.Where(c => c.rarity == rarity).ToList();
            if (pool.Count == 0)
                return;
            for (int i = 0; i < count; i++)
            {
                pack.Add(pool[_rng.Next(pool.Count)]);
            }
        }
    }
}
