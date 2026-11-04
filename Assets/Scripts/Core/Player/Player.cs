using System;

namespace Pantheon.Core
{
    public class Player
    {
        public PlayerSlot Slot { get; }
        public Hero Hero { get; }
        public Deck Deck { get; }
        public PlayerHand Hand { get; }

        public Player(PlayerSlot slot, Deck deck)
        {
            if (deck == null)
                throw new ArgumentNullException(nameof(deck));

            Slot = slot;
            Deck = deck;
            Hero = new Hero();
            Hand = new PlayerHand();
        }
    }
}
