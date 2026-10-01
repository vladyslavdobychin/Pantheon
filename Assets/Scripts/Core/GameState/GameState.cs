using System;
using System.Collections.Generic;

namespace Pantheon.Core
{
    public class GameState : IGameState
    {
        private readonly IAgent[] agents;

        public GameState(IAgent[] agents)
        {
            this.agents = agents;
            // Initialize board, decks, players etc.
        }

        public bool HasWinner()
        {
            // If either player has HP = 0
            return false;
        }

        public int GetCurrentAgentIndex()
        {
            return 0;
        }

        public List<GameEvent> ApplyMove(IMove move)
        {
            return new List<GameEvent>();
        }
    }
}
