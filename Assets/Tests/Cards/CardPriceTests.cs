using NUnit.Framework;
using Pantheon.Core.Cards;
using System;

namespace Pantheon.CoreTests.Cards
{
    public class CardPriceTests
    {
        [Test]
        public void Constructor_ZeroAmount_IsAllowed()
        {
            var price = new CardPrice(0);

            Assert.That(price.Amount, Is.EqualTo(0));
        }

        [TestCase(-1)]
        [TestCase(-100)]
        public void Constructor_NegativeAmount_Throws(int amount)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardPrice(amount));
        }

        [Test]
        public void Zero_IsAPriceOfZero()
        {
            Assert.That(CardPrice.Zero.Amount, Is.EqualTo(0));
        }

        [Test]
        public void SamePrices_AreEqual()
        {
            Assert.That(new CardPrice(3), Is.EqualTo(new CardPrice(3)));
        }

        [Test]
        public void DifferentPrices_AreNotEqual()
        {
            Assert.That(new CardPrice(3), Is.Not.EqualTo(new CardPrice(4)));
        }

        [Test]
        public void Default_IsZero()
        {
            CardPrice price = default;

            Assert.That(price.Amount, Is.EqualTo(0));
        }

        [Test]
        public void ToString_ReturnsAmount()
        {
            Assert.That(new CardPrice(7).ToString(), Is.EqualTo("7"));
        }
    }
}
