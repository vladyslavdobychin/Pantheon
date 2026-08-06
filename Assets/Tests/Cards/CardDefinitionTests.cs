using System;
using NUnit.Framework;
using Pantheon.Core.Cards;

namespace Pantheon.CoreTests.Cards
{
    public class CardDefinitionTests
    {
        private const string CardName = "Hoplite";
        private const int CardAttack = 3;
        private const int CardHealth = 2;

        private static CardDefinition Build(
            string name = CardName,
            int priceAmount = 2,
            CardType type = null,
            int attack = CardAttack,
            int health = CardHealth) =>
            new(name, new CardPrice(priceAmount), type ?? CardType.Creature, attack, health);

        [Test]
        public void Constructor_ValidArguments_DoesNotSwapAttackAndHealth()
        {
            var definition = Build();

            Assert.That(definition.Attack, Is.EqualTo(CardAttack));
            Assert.That(definition.Health, Is.EqualTo(CardHealth));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Constructor_MissingName_Throws(string name)
        {
            Assert.Throws<ArgumentException>(() => Build(name: name));
        }

        [Test]
        public void Constructor_NullType_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new CardDefinition(CardName, new CardPrice(2), null, CardAttack, CardHealth));
        }

        [Test]
        public void Constructor_NegativeAttack_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(attack: -1));
        }

        [Test]
        public void Constructor_NegativeHealth_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(health: -1));
        }

        [Test]
        public void Constructor_ZeroAttackAndHealth_IsAllowed()
        {
            var definition = Build(attack: 0, health: 0);

            Assert.That(definition.Attack, Is.EqualTo(0));
            Assert.That(definition.Health, Is.EqualTo(0));
        }

    }
}
