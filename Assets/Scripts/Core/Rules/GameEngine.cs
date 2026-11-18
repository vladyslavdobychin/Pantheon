using System;
using System.Collections.Generic;

namespace Pantheon.Core
{
    /// <summary>
    /// The synchronous step function of AGENTS.md decision 4, and Core's single front door:
    /// StartGame / GetValidMoves / Apply plus the read-only result queries. The GameRunner, the
    /// headless server and the AI search talk to this and nothing else — they never wire up
    /// MoveGenerator, MoveResolver and GameRules themselves.
    ///
    /// Stateless on purpose: GameState is a parameter, never a field, so one instance serves the
    /// live game and every clone an AI search spawns (clone -> Apply -> score -> discard).
    ///
    /// It owns StartGame because that is the only state change no move causes. Everything else it
    /// delegates: the rules live below it, not in it.
    /// </summary>
    public class GameEngine
    {
        private readonly MoveGenerator generator;
        private readonly MoveResolver resolver;
        private readonly GameRules rules;

        public GameEngine(MoveGenerator generator, MoveResolver resolver, GameRules rules)
        {
            if (generator == null)
                throw new ArgumentNullException(nameof(generator));

            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            this.generator = generator;
            this.resolver = resolver;
            this.rules = rules;
        }

        /// <summary>
        /// Opens the first turn. Separate from Apply because no move causes it.
        /// </summary>
        public List<GameEvent> StartGame(GameState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var events = new List<GameEvent>
            {
                new GameStarted(state.GetCurrentPlayer(), state.Seed)
            };

            resolver.BeginTurn(state, events);

            return Record(state, events);
        }

        /// <summary>Read-only. Safe for a thin client to call for UI affordances.</summary>
        public IReadOnlyList<Move> GetValidMoves(GameState state) => generator.GetValidMoves(state);

        public List<GameEvent> Apply(GameState state, Move move)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (move == null)
                throw new ArgumentNullException(nameof(move));

            if (rules.IsOver(state))
                throw new InvalidOperationException("Cannot apply a move to a finished game.");

            var events = new List<GameEvent>();

            resolver.Resolve(state, move, events);

            var result = rules.GetResult(state);
            if (result != null)
                events.Add(new GameEnded(result));

            return Record(state, events);
        }

        /// <summary>Null while the game is still running.</summary>
        public GameResult GetResult(GameState state) => rules.GetResult(state);

        public bool IsOver(GameState state) => rules.IsOver(state);

        private static List<GameEvent> Record(GameState state, List<GameEvent> events)
        {
            foreach (var gameEvent in events)
                state.RecordEvent(gameEvent);

            return events;
        }
    }
}
