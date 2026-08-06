using NUnit.Framework;
using Pantheon.Core.Cards;
using System;

namespace Pantheon.CoreTests.Cards
{
    public class CardInstanceTests
    {
        private const int CardStartHealth = 2;
        private const int CardAttack = 3;
        private const int CardId = 1;

        private static CardDefinition TestCardDefinition() => new(
            "Hoplite",
            new CardPrice(2),
            CardType.Creature,
            CardAttack,
            CardStartHealth
        );

        private static CardInstance TestCardInstance() => new(CardId, TestCardDefinition());

        [Test]
        public void Constructor_ReceivesNullForDefinition_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new CardInstance(CardId, null)
            );
        }

        [Test]
        public void Constructor_NewInstance_StartsAtFullHealthAndSummoningSick()
        {
            var instance = TestCardInstance();

            Assert.That(instance.CurrentHealth, Is.EqualTo(CardStartHealth));
            Assert.That(instance.IsSummoningSick, Is.True);
            Assert.That(instance.CanAttack, Is.False);
        }

        [Test]
        public void TakeDamage_LessThanMaxHealth_ReducesHealth()
        {
            var instance = TestCardInstance();
            var damageDealt = 1;

            instance.TakeDamage(damageDealt);

            Assert.That(instance.CurrentHealth, Is.EqualTo(CardStartHealth - damageDealt));
        }

        [Test]
        public void TakeDamage_DamageEqualsToHealth_KillsInstance()
        {
            var instance = TestCardInstance();
            var damageDealt = CardStartHealth;

            instance.TakeDamage(damageDealt);

            Assert.That(instance.CurrentHealth, Is.EqualTo(0));
            Assert.That(instance.IsDead, Is.True);
        }

        [Test]
        public void TakeDamage_DamageBiggerThanHealth_HealthFloorsAtZero()
        {
            var instance = TestCardInstance();
            var damageDealt = 100;

            instance.TakeDamage(damageDealt);

            Assert.That(instance.CurrentHealth, Is.EqualTo(0));
            Assert.That(instance.IsDead, Is.True);
        }

        [Test]
        public void TakeDamage_CannotTakeNegativeNumber_ThrowsException()
        {
            var instance = TestCardInstance();
            var damageDealt = -1;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => instance.TakeDamage(damageDealt)
            );
        }

        [Test]
        public void BeginTurn_OnNextTurn_ClearsSummoningSick()
        {
            var instance = TestCardInstance();

            instance.BeginTurn();

            Assert.That(instance.IsSummoningSick, Is.False);
            Assert.That(instance.CanAttack, Is.True);
        }

        [Test]
        public void TwoInstances_FromSameDefinition_HaveIndependentHealth()
        {
            var definition = TestCardDefinition();
            var instance1 = new CardInstance(1, definition);
            var instance2 = new CardInstance(2, definition);
            var damageDealt = 1;

            instance1.TakeDamage(damageDealt);

            Assert.That(instance1.CurrentHealth, Is.Not.EqualTo(instance2.CurrentHealth));
        }
    }

}
