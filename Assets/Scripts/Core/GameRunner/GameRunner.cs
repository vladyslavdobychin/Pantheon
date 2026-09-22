using System;

namespace Pantheon.Core
{
    public class GameRunner : IGameRunner
    {
        private GameStateFactory stateFactory;
        private IGameState gameState;
        private IAgent[] agents;

        public GameRunner(GameStateFactory factory, IAgent[] agents)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            if (agents == null)
                throw new ArgumentNullException(nameof(agents));

            if (agents.Length != 2)
                throw new ArgumentException("Exactly two instances of IAgent are required.", nameof(agents));

            stateFactory = factory;
            this.agents = agents;

            gameState = stateFactory.Create(agents);
        }

        public IGameState Run()
        {
            while (!gameState.HasWinner())
            {
                /*
                The engine is running in loops, and this loop should also be agnostic to at which state the game currently is.
                Meaning we should be able to suspend the loop for whater the reason and continue it without breaking the overall
                state of the game

                1. Game state was initialised at the constructor, so I think we can just return it at the start of the loop for
                view to render
                2. We somehow need to await the response from the agents and when both promises are completed - adanvce the loop?
                */
            }

            /*
            The block above should be running on repeat,
            when we have a winner it stops so we need to do some sort of a final return? Final resolved state?
            */

            return gameState;
        }
    }

    /*
    NOTES:
    1. Should I add constructor() that receives both agents and game state factory to avoid shadow dependencies?
    I still have an open question about *what* initializes GameRunner in this case, what passes those parameters.
    2.
    */
}
