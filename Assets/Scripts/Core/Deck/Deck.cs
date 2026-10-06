using System;
using System.Collections.Generic;
using System.Linq;
using Pantheon.Core.Cards;

namespace Pantheon.Core
{
    public class Deck : IDeck
    {
        public const int DeckSize = 30;
        private readonly List<CardDefinition> cards;

        public Deck(IReadOnlyList<CardDefinition> cards)
        {
            // TODO: Add max 2 identical cards rule
            if (cards == null)
                throw new ArgumentNullException(nameof(cards));

            if (cards.Count != DeckSize)
                throw new ArgumentException($"Deck must contain exactly {DeckSize} cards.", nameof(cards));

            this.cards = new List<CardDefinition>(cards);
        }

        public static IDeck TestDeck()
        {
            return new Deck(Enumerable.Repeat(CardCatalog.Hoplite, DeckSize).ToList());
        }
    }
}
