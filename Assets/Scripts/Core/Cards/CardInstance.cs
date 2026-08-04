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

            CurrentHealth = definition.Health;   // starts at full
            IsSummoningSick = true;              // cannot attack the turn it enters play
            HasAttackedThisTurn = false;
        }

        // Stats that never change are read straight from the blueprint, not copied.
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

        // The engine calls this at the start of the controlling player's turn.
        public void OnTurnStart()
        {
            IsSummoningSick = false;
            HasAttackedThisTurn = false;
        }

        public override string ToString() => $"{Definition.Name} ({Attack}/{CurrentHealth}) #{Id}";
    }
}
