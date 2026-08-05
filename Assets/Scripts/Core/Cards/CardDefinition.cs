using System;

namespace Pantheon.Core.Cards
{
    public sealed class CardDefinition
    {
        public string Name { get; }
        public CardPrice Price { get; }
        public CardType Type { get; }
        public int Attack { get; }
        public int Health { get; }

        public CardDefinition(string name, CardPrice price, CardType type, int attack, int health)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Card name must not be empty.", nameof(name));

            if (type is null)
                throw new ArgumentNullException(nameof(type));

            if (attack < 0)
                throw new ArgumentOutOfRangeException(nameof(attack), attack, "Attack cannot be negative.");

            if (health < 0)
                throw new ArgumentOutOfRangeException(nameof(health), health, "Health cannot be negative");

            Name = name;
            Price = price;
            Type = type;
            Attack = attack;
            Health = health;
        }

        public override string ToString() => $"{Name} [{Type.Name}] {Attack}/{Health} cost {Price}";
    }
}
