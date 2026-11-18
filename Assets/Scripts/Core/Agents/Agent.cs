using System;
using System.Threading.Tasks;

namespace Pantheon.Core
{
    /// <summary>
    /// Placeholder brain: always takes the first legal move. Returns a completed task,
    /// keeping the async seam free of real waiting.
    ///
    /// Reads the board through GameEngine, the same front door a remote client would use.
    /// </summary>
    public class Agent : IAgent
    {
        private readonly GameEngine engine;

        public Agent(GameEngine engine)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            this.engine = engine;
        }

        public Task<Move> DecideAsync(GameState gameState)
        {
            var moves = engine.GetValidMoves(gameState);

            if (moves.Count == 0)
                throw new InvalidOperationException("No legal moves available.");

            return Task.FromResult(moves[0]);
        }
    }
}
