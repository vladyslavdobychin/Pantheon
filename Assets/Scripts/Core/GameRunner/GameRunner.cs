using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pantheon.Core
{
    public class GameRunner
    {
        /// <summary>Safety valve: a stalled board or a rules bug fails loudly instead of hanging.</summary>
        public const int MaxTurns = 1000;

        private readonly GameStateFactory stateFactory;
        private readonly MoveGenerator moveGenerator;
        private readonly MoveApplier moveApplier;
        private readonly GameRules rules;
        private readonly GameSetup setup;

        private GameState gameState;

        public GameRunner(
            GameStateFactory factory,
            MoveGenerator moveGenerator,
            MoveApplier moveApplier,
            GameRules rules,
            GameSetup setup)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            if (moveGenerator == null)
                throw new ArgumentNullException(nameof(moveGenerator));

            if (moveApplier == null)
                throw new ArgumentNullException(nameof(moveApplier));

            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            if (setup == null)
                throw new ArgumentNullException(nameof(setup));

            stateFactory = factory;
            this.moveGenerator = moveGenerator;
            this.moveApplier = moveApplier;
            this.rules = rules;
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

            Dispatch(moveApplier.StartGame(gameState));

            while (!rules.IsOver(gameState))
            {
                if (gameState.TurnNumber > MaxTurns)
                    throw new InvalidOperationException(
                        $"Game exceeded {MaxTurns} turns without finishing."
                    );

                var agent = setup.GetPlayerSetup(gameState.GetCurrentPlayer()).Agent;
                var move = await agent.DecideAsync(gameState);

                Dispatch(moveApplier.Apply(gameState, move));
            }

            return rules.GetResult(gameState);
        }

        private void Dispatch(List<GameEvent> events)
        {
            // TODO: hand events to the view / per-recipient filter for netcode.
        }
    }
}
