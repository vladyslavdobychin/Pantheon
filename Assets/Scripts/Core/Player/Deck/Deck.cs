using System;
using System.Collections.Generic;
using Pantheon.Core.Cards;

namespace Pantheon.Core
{
    /// <summary>
    /// Runtime draw pile: shuffled, shrinking, holds CardInstance. Built from a DeckDefinition
    /// by GameState so the shuffle uses the seeded Rng.
    /// </summary>
    public class Deck
    {
        private readonly List<CardInstance> cards;

        public Deck(IReadOnlyList<CardInstance> cards)
        {
            if (cards == null)
                throw new ArgumentNullException(nameof(cards));

            this.cards = new List<CardInstance>(cards);
        }

        public int Count => cards.Count;

        public bool IsEmpty => cards.Count == 0;

        internal CardInstance Draw()
        {
            if (IsEmpty)
                throw new InvalidOperationException("Cannot draw from an empty deck.");

            var index = cards.Count - 1;
            var card = cards[index];
            cards.RemoveAt(index);

            return card;
        }
    }
}
