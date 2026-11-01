using System;
using System.Reflection.Metadata;

namespace Pantheon.Core
{
    public class Player
    {
        public PlayerSlot Slot { get; }
        public Deck Deck { get; }
        public PlayerHand Hand { get; }
        public Player(PlayerSlot slot, Deck deck)
        {
            Slot = slot;
            Deck = deck;
            Hand = new PlayerHand(null);
        }
    }
}
