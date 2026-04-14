using System;
using System.Collections.Generic;
using DualCraft.Cards;

namespace DualCraft.Collection
{
    /// <summary>
    /// Tracks how many copies of each card a player owns.  A collection is
    /// independent of any specific deck and does not enforce deck‑building
    /// rules.  It can be used to power a collection browser or to reward
    /// players with new cards when opening booster packs.
    /// </summary>
    public class CardCollection
    {
        private readonly Dictionary<CardData, int> _cards = new();

        /// <summary>
        /// Adds one or more copies of a card to the collection.  If the card
        /// already exists in the dictionary its count is incremented.
        /// </summary>
        /// <param name="card">The card to add.  May not be null.</param>
        /// <param name="amount">Number of copies to add.  Defaults to 1.</param>
        public void AddCard(CardData card, int amount = 1)
        {
            if (card == null || amount <= 0)
                return;
            if (_cards.ContainsKey(card))
            {
                _cards[card] += amount;
            }
            else
            {
                _cards[card] = amount;
            }
        }

        /// <summary>
        /// Removes one or more copies of a card from the collection.  If the
        /// player does not own enough copies, the method returns false and
        /// nothing is removed.  When a card's count reaches zero it is
        /// removed entirely from the collection.
        /// </summary>
        public bool RemoveCard(CardData card, int amount = 1)
        {
            if (card == null || amount <= 0)
                return false;
            if (!_cards.TryGetValue(card, out int current) || current < amount)
                return false;
            _cards[card] = current - amount;
            if (_cards[card] <= 0)
            {
                _cards.Remove(card);
            }
            return true;
        }

        /// <summary>
        /// Returns how many copies of the specified card the player owns.  If
        /// the card does not exist in the collection the count is zero.
        /// </summary>
        public int GetCount(CardData card)
        {
            return card != null && _cards.TryGetValue(card, out int count) ? count : 0;
        }

        /// <summary>
        /// Enumerates every card in the collection along with the number of
        /// copies owned.  Useful for displaying the collection in a UI.
        /// </summary>
        public IEnumerable<KeyValuePair<CardData, int>> GetAllCards()
        {
            foreach (var kvp in _cards)
            {
                yield return kvp;
            }
        }
    }
}
