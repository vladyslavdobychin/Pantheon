using System;
using System.Collections.Generic;

namespace Pantheon.Core
{
    /// <summary>
    /// Enumerates every legal move for the current player. Read-only: never mutates state.
    /// Stateless on purpose — one instance serves every clone in an AI search.
    /// </summary>
    public class MoveGenerator
    {
        public IReadOnlyList<Move> GetValidMoves(GameState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            // Fatigue slice: ending the turn is the only thing a player can do.
            return new Move[] { new EndTurnMove() };
        }
    }
}
