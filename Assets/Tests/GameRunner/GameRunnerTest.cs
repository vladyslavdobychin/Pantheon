using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pantheon.Core;

namespace Pantheon.CoreTests
{
    public class GameRunnerTest
    {
        private const int Seed = 123;

        private static IAgent[] TwoAgents() => new IAgent[] { new Agent(), new Agent() };

        private static IDeck[] TwoDecks() => new IDeck[] { Deck.TestDeck(), Deck.TestDeck() };

        private static IEnumerable<TestCaseData> InvalidConstructorArguments()
        {
            var factory = new GameStateFactory();

            yield return new TestCaseData(null, TwoAgents(), TwoDecks(), typeof(ArgumentNullException))
                .SetName("Constructor_NullFactory_Throws");

            yield return new TestCaseData(factory, null, TwoDecks(), typeof(ArgumentNullException))
                .SetName("Constructor_NullAgents_Throws");

            yield return new TestCaseData(factory, new IAgent[] { new Agent() }, TwoDecks(), typeof(ArgumentException))
                .SetName("Constructor_OneAgent_Throws");

            yield return new TestCaseData(factory, new IAgent[] { new Agent(), new Agent(), new Agent() }, TwoDecks(), typeof(ArgumentException))
                .SetName("Constructor_ThreeAgents_Throws");

            yield return new TestCaseData(factory, TwoAgents(), null, typeof(ArgumentNullException))
                .SetName("Constructor_NullDecks_Throws");

            yield return new TestCaseData(factory, TwoAgents(), new IDeck[] { Deck.TestDeck() }, typeof(ArgumentException))
                .SetName("Constructor_OneDeck_Throws");
        }

        [TestCaseSource(nameof(InvalidConstructorArguments))]
        public void Constructor_InvalidArguments_Throws(
            GameStateFactory factory,
            IAgent[] agents,
            IDeck[] decks,
            Type expectedException)
        {
            Assert.Throws(expectedException, () => new GameRunner(factory, agents, decks, Seed));
        }

        [Test]
        public void Initialise_ReturnsValidState()
        {
            var gameRunner = new GameRunner(new GameStateFactory(), TwoAgents(), TwoDecks(), Seed);

            var gameState = gameRunner.Initialise();

            Assert.IsInstanceOf<IGameState>(gameState);
        }
    }
}
