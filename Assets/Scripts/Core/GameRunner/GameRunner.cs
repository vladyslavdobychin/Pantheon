using System;
using System.Threading.Tasks;

namespace Pantheon.Core
{
    public class GameRunner : IGameRunner
    {
        private GameStateFactory stateFactory;
        private IGameState gameState;
        private IAgent[] agents;
        private IDeck[] decks;
        private int seed;

        public GameRunner(GameStateFactory factory, IAgent[] agents, IDeck[] decks, int seed)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            if (agents == null)
                throw new ArgumentNullException(nameof(agents));

            if (agents.Length != 2)
                throw new ArgumentException("Exactly two instances of IAgent are required.", nameof(agents));

            stateFactory = factory;
            this.agents = agents;
            this.decks = decks;
            this.seed = seed;
        }

        public IGameState Initialise()
        {
            return gameState = stateFactory.Create(agents, decks, seed);
        }

        public async Task<IGameState> RunAsync()
        {
            while (!gameState.HasWinner())
            {
                var currentAgent = agents[gameState.GetCurrentAgentIndex()];
                var move = await currentAgent.DecideAsync(gameState);
                var events = gameState.ApplyMove(move);

                // TODO: Return events to view
            }

            return gameState;
        }
    }

}
