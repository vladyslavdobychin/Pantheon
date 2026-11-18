using System;

namespace Pantheon.Core
{
    public class Hero
    {
        public const int StartingHealth = 30;

        public int MaxHealth { get; }
        public int Health { get; private set; }

        public Hero() : this(StartingHealth)
        {
        }

        public Hero(int maxHealth)
        {
            if (maxHealth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxHealth), maxHealth, "Hero max health must be positive.");

            MaxHealth = maxHealth;
            Health = maxHealth;
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
