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
    }
}
