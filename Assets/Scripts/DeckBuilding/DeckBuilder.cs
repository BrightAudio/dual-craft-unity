using System;
using System.Collections.Generic;
using System.Linq;
using DualCraft.Cards;
using DualCraft.Collection;
using DualCraft.Core;

namespace DualCraft.DeckBuilding
{
    /// <summary>
    /// Builds a playable deck from a player's card collection.  The deck
    /// builder enforces overall deck size and per‑card copy limits.  It
    /// exposes methods to add and remove cards from a working deck list and
    /// to validate the final build before use.  This class does not handle
    /// user interface; an editor or menu should call these methods based on
    /// player selections.
    /// </summary>
    public class DeckBuilder
    {
        private readonly CardCollection _collection;
        private readonly List<CardData> _draft = new();

        public DeckBuilder(CardCollection collection)
        {
            _collection = collection;
        }

        /// <summary>
        /// An immutable view of the cards currently in the draft deck.  Use
        /// this to display the current deck contents in a UI.
        /// </summary>
        public IReadOnlyList<CardData> Draft => _draft;

        /// <summary>
        /// Attempts to add the specified card to the draft deck.  The method
        /// returns false if any rule would be violated: the deck is already
        /// full, the player does not own enough copies of the card, or the
        /// card would exceed the per‑card copy limit defined in
        /// <see cref="GameConstants.MaxCardCopies"/>.
        /// </summary>
        public bool AddCard(CardData card)
        {
            if (card == null)
                return false;
            // Check deck size
            if (_draft.Count >= GameConstants.DeckSize)
                return false;
            // Check collection ownership
            int owned = _collection.GetCount(card);
            int alreadyInDeck = _draft.Count(c => ReferenceEquals(c, card));
            if (alreadyInDeck >= owned)
                return false;
            // Enforce max copies per card
            if (alreadyInDeck >= GameConstants.MaxCardCopies)
                return false;
            _draft.Add(card);
            return true;
        }

        /// <summary>
        /// Removes one copy of the specified card from the draft deck.  If the
        /// card is not present in the draft the method returns false.
        /// </summary>
        public bool RemoveCard(CardData card)
        {
            int index = _draft.FindIndex(c => ReferenceEquals(c, card));
            if (index < 0)
                return false;
            _draft.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Validates the draft deck to ensure it meets the required deck size
        /// and copy limits.  If the deck is invalid the method returns false
        /// and sets an error message explaining the problem.
        /// </summary>
        public bool Validate(out string error)
        {
            error = null;
            if (_draft.Count != GameConstants.DeckSize)
            {
                error = $"Deck must contain exactly {GameConstants.DeckSize} cards.";
                return false;
            }
            var groups = _draft.GroupBy(c => c);
            foreach (var group in groups)
            {
                if (group.Count() > GameConstants.MaxCardCopies)
                {
                    error = $"Too many copies of {group.Key.cardName}. Max {GameConstants.MaxCardCopies}.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Finalizes the deck by returning a new list containing the draft
        /// cards and clearing the draft.  You should call
        /// <see cref="Validate"/> before finalizing to ensure the deck meets
        /// all requirements.
        /// </summary>
        public List<CardData> Build()
        {
            var finalDeck = new List<CardData>(_draft);
            _draft.Clear();
            return finalDeck;
        }
    }
}
