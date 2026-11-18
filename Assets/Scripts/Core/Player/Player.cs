using System;

namespace Pantheon.Core
{
    public class Player
    {
        public PlayerSlot Slot { get; }
        public DeckDefinition DeckDefinition { get; }
        public Hero Hero { get; }
        public PlayerHand Hand { get; }

        /// <summary>Runtime draw pile. Assigned by GameState, which owns the seeded Rng.</summary>
        public Deck Deck { get; private set; }

        /// <summary>How many times this player has drawn from an empty deck.</summary>
        public int FatigueCounter { get; private set; }

        public Player(PlayerSlot slot, DeckDefinition deckDefinition)
        {
            if (deckDefinition == null)
                throw new ArgumentNullException(nameof(deckDefinition));

            Slot = slot;
            DeckDefinition = deckDefinition;
            Hero = new Hero();
            Hand = new PlayerHand();
        }

        internal void SetDeck(Deck deck)
        {
            if (deck == null)
                throw new ArgumentNullException(nameof(deck));

            Deck = deck;
        }

        internal void IncreaseFatigue()
        {
            FatigueCounter++;
        }
    }
}
