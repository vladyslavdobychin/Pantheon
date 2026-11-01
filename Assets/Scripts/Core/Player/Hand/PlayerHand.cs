using System;
using System.Collections.Generic;
using Pantheon.Core.Cards;

namespace Pantheon.Core
{
    public class PlayerHand
    {
        public const int HandSize = 10;
        private readonly List<CardInstance> cards;

        public PlayerHand() : this(Array.Empty<CardInstance>())
        {
        }

        public PlayerHand(IReadOnlyList<CardInstance> cards)
        {
            if (cards == null)
                throw new ArgumentNullException(nameof(cards));

            this.cards = new List<CardInstance>(cards);
        }

        public int Count => cards.Count;

        public IReadOnlyList<CardInstance> Cards => cards;
    }
}
