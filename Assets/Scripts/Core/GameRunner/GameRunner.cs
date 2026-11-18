using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pantheon.Core
{
    /// <summary>
    /// Loop glue, nothing else: ask the current agent for a move, hand it to the engine, push the
    /// events out, repeat until someone wins. Rules live in GameEngine and below.
    /// </summary>
    public class GameRunner
    {
        /// <summary>Safety valve: a stalled board or a rules bug fails loudly instead of hanging.</summary>
        public const int MaxTurns = 1000;

        private readonly GameStateFactory stateFactory;
        private readonly GameEngine engine;
        private readonly GameSetup setup;

        private GameState gameState;

        public GameRunner(GameStateFactory factory, GameEngine engine, GameSetup setup)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            if (setup == null)
                throw new ArgumentNullException(nameof(setup));

            stateFactory = factory;
            this.engine = engine;
            this.setup = setup;
        }

        public GameState Initialise()
        {
            return gameState = stateFactory.Create(setup);
        }

        public async Task<GameResult> RunAsync()
        {
            if (gameState == null)
                throw new InvalidOperationException("Initialise must be called before RunAsync.");

            Dispatch(engine.StartGame(gameState));

            while (!engine.IsOver(gameState))
            {
                if (gameState.TurnNumber > MaxTurns)
                    throw new InvalidOperationException(
                        $"Game exceeded {MaxTurns} turns without finishing."
                    );

                var agent = setup.GetPlayerSetup(gameState.GetCurrentPlayer()).Agent;
                var move = await agent.DecideAsync(gameState);

                Dispatch(engine.Apply(gameState, move));
            }

            return engine.GetResult(gameState);
        }

        private void Dispatch(List<GameEvent> events)
        {
            // TODO: hand events to the view / per-recipient filter for netcode.
        }
    }
}
