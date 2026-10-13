using System;
using System.Threading.Tasks;

namespace Pantheon.Core
{
    public class GameRunner
    {
        private GameStateFactory stateFactory;
        private GameState gameState;
        private GameSetup setup;

        public GameRunner(GameStateFactory factory, GameSetup setup)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            if (setup == null)
                throw new ArgumentNullException(nameof(setup));

            stateFactory = factory;
            this.setup = setup;
        }

        public GameState Initialise()
        {
            return gameState = stateFactory.Create(setup);
        }

        public async Task<GameState> RunAsync()
        {
            while (!gameState.HasWinner())
            {
                var agent = setup.GetPlayerSetup(gameState.GetCurrentPlayer()).Agent;
                // var move = await currentAgent.DecideAsync(GameState);
                // var events = GameState.ApplyMove(move);

                // TODO: Return events to view
            }

            return gameState;
        }
    }

}
