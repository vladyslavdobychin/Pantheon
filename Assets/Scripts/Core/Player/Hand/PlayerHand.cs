using System;
using System.Collections.Generic;
using Pantheon.Core.Cards;

namespace Pantheon.Core
{
    public class PlayerHand
    {
        public const int HandSize = 10;
        private readonly List<CardDefinition> cards;

        public PlayerHand(IReadOnlyList<CardDefinition>cards)
        {
            // Hand can be empty

            if (cards.Count > HandSize)
                throw new ArgumentOutOfRangeException($"Hand must contain not more than {HandSize} cards.", nameof(cards));

            this.cards = new List<CardDefinition>(cards);
        }

    }
}
