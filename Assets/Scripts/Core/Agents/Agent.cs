using System;
using System.Threading.Tasks;

namespace Pantheon.Core
{
    /// <summary>
    /// Placeholder brain: always takes the first legal move. Returns a completed task,
    /// keeping the async seam free of real waiting.
    /// </summary>
    public class Agent : IAgent
    {
        private readonly MoveGenerator moveGenerator;

        public Agent(MoveGenerator moveGenerator)
        {
            if (moveGenerator == null)
                throw new ArgumentNullException(nameof(moveGenerator));

            this.moveGenerator = moveGenerator;
        }

        public Task<Move> DecideAsync(GameState gameState)
        {
            var moves = moveGenerator.GetValidMoves(gameState);

            if (moves.Count == 0)
                throw new InvalidOperationException("No legal moves available.");

            return Task.FromResult(moves[0]);
        }
    }
}
