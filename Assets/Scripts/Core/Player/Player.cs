using System;

namespace Pantheon.Core
{
    public class Player
    {
        public PlayerSlot Slot { get; }
        public Deck Deck { get; }
        public Player(PlayerSlot slot, Deck deck)
        {
            Slot = slot;
            Deck = deck;
        }
    }
}
