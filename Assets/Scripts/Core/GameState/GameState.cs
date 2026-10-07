using System;
using System.Collections.Generic;

namespace Pantheon.Core
{
    public class GameState : IGameState
    {
        private readonly IAgent[] agents;
        private readonly IDeck[] decks;
        private readonly Random random;
        private readonly int currentAgentIndex;

        public GameState(IAgent[] agents, IDeck[] decks, int seed)
        {
            this.agents = agents;
            this.decks = decks;
            this.random = new Random(seed);
            this.currentAgentIndex = this.random.Next(2);

            /*
            [X] Initialize player indexes
            [X] Initialize decks
            [ ] Initialize board
            */
        }

        public bool HasWinner()
        {
            // If either player has HP = 0
            return false;
        }

        public int GetCurrentAgentIndex()
        {
            return currentAgentIndex;
        }

        public List<GameEvent> ApplyMove(IMove move)
        {
            return new List<GameEvent>();
        }
    }
}
