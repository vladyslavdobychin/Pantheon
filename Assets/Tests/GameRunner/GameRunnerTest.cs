using System;
using NUnit.Framework;
using Pantheon.Core;

namespace Pantheon.CoreTests
{
    public class GameRunnerTest
    {
        [Test]
        public void Constructor_IfNoAgentsProvided_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new GameRunner(new GameStateFactory(), null));
        }

        [Test]
        public void Constructor_IfNoFactoryProvided_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new GameRunner(
                        null,
                        new IAgent[] { new Agent(), new Agent() }
                    )
                );
        }

        [Test]
        public void Constructor_IfIncorrectNumberOfAgents_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => new GameRunner(
                        new GameStateFactory(),
                        new IAgent[] { new Agent() }
                    )
                );
        }

        [Test]
        public void Initialise_ReturnsValidState()
        {
            var gameRunner = new GameRunner(new GameStateFactory(),  new IAgent[] { new Agent(), new Agent() });
            var gameState = gameRunner.Initialise();

            Assert.IsInstanceOf<IGameState>(gameState);
        }
    }
}
