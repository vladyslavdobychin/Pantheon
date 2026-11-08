using NUnit.Framework;
using Pantheon.Core;

namespace Pantheon.CoreTests
{
    public class MoveGeneratorTests
    {
        private const int Seed = 123;

        private static GameState NewState() => new GameState(
            new Player(PlayerSlot.First, DeckDefinition.TestDeck()),
            new Player(PlayerSlot.Second, DeckDefinition.TestDeck()),
            Seed
        );

        [Test]
        public void GetValidMoves_FreshState_ReturnsOnlyEndTurn()
        {
            var state = NewState();
            var generator = new MoveGenerator();

            var moves = generator.GetValidMoves(state);

            Assert.That(moves, Is.EquivalentTo(new Move[] { new EndTurnMove() }));
        }
    }
}
