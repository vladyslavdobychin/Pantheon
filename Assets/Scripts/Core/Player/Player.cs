using System;

namespace Pantheon.Core
{
    public class Player
    {
        public const int StartingHealth = 30;

        public PlayerSlot Slot { get; }
        public Deck Deck { get; }
        public PlayerHand Hand { get; }
        public int Health { get; private set; } = StartingHealth;

        public Player(PlayerSlot slot, Deck deck)
        {
            if (deck == null)
                throw new ArgumentNullException(nameof(deck));

            Slot = slot;
            Deck = deck;
            Hand = new PlayerHand();
        }

        public bool IsDead => Health <= 0;

        public void TakeDamage(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Damage cannot be negative.");

            Health = Math.Max(0, Health - amount);
        }
    }
}
