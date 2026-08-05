using System;

namespace Pantheon.Core.Cards
{
    public sealed class CardInstance
    {
        public int Id { get; }
        public CardDefinition Definition { get; }

        public int CurrentHealth { get; private set; }
        public bool IsSummoningSick { get; private set; }
        public bool HasAttackedThisTurn { get; private set; }

        public CardInstance(int id, CardDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Id = id;

            CurrentHealth = definition.Health;
            IsSummoningSick = true;
            HasAttackedThisTurn = false;
        }

        public int Attack => Definition.Attack;
        public int MaxHealth => Definition.Health;

        public bool IsDead => CurrentHealth <= 0;
        public bool CanAttack => !IsSummoningSick && !HasAttackedThisTurn && !IsDead;

        public void TakeDamage(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Damage cannot be negative.");

            CurrentHealth = Math.Max(0, CurrentHealth - amount);
        }

        public void MarkAttacked() => HasAttackedThisTurn = true;

        public void OnTurnStart()
        {
            IsSummoningSick = false;
            HasAttackedThisTurn = false;
        }

        public override string ToString() => $"{Definition.Name} ({Attack}/{CurrentHealth}) #{Id}";
    }
}
