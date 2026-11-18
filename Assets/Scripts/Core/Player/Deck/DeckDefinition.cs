using System;
using System.Collections.Generic;
using System.Linq;
using Pantheon.Core.Cards;

namespace Pantheon.Core
{
    /// <summary>
    /// Immutable blueprint: the list a player built before the match. Never mutated during play.
    /// Mirrors CardDefinition / CardInstance — the runtime counterpart is Deck.
    /// </summary>
    public class DeckDefinition
    {
        public const int DeckSize = 30;

        private readonly List<CardDefinition> cards;

        public DeckDefinition(IReadOnlyList<CardDefinition> cards)
        {
            // TODO: Add max 2 identical cards rule
            if (cards == null)
                throw new ArgumentNullException(nameof(cards));

            if (cards.Count != DeckSize)
                throw new ArgumentException($"Deck must contain exactly {DeckSize} cards.", nameof(cards));

            this.cards = new List<CardDefinition>(cards);
        }

        public IReadOnlyList<CardDefinition> Cards => cards;

        public static DeckDefinition TestDeck()
        {
            return new DeckDefinition(Enumerable.Repeat(CardCatalog.Hoplite, DeckSize).ToList());
        }
    }
}
