using System;
using System.Collections.Generic;
using System.Linq;

namespace Pantheon.Core
{
    /// <summary>
    /// A move happened: mutate state and say what changed.
    ///
    /// "Resolve" rather than "apply" because a move cascades — EndTurnMove ends the turn, which
    /// opens the next one, which draws, which may fatigue, which may kill a hero. Turn transitions
    /// live here precisely because a move is what causes them.
    ///
    /// Stateless: state comes in as a parameter so one instance serves every clone in an AI search.
    /// </summary>
    public class MoveResolver
    {
        private readonly MoveGenerator generator;
        private readonly Effects effects;

        public MoveResolver(MoveGenerator generator, Effects effects)
        {
            if (generator == null)
                throw new ArgumentNullException(nameof(generator));

            if (effects == null)
                throw new ArgumentNullException(nameof(effects));

            this.generator = generator;
            this.effects = effects;
        }

        public void Resolve(GameState state, Move move, List<GameEvent> events)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (move == null)
                throw new ArgumentNullException(nameof(move));

            if (events == null)
                throw new ArgumentNullException(nameof(events));

            // Never trust the move: a remote client can send anything.
            if (!generator.GetValidMoves(state).Contains(move))
                throw new InvalidOperationException($"Illegal move: {move}");

            switch (move)
            {
                case EndTurnMove:
                    EndTurn(state, events);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(move), move, "Unhandled move type.");
            }
        }

        /// <summary>
        /// Opens a turn. Internal rather than private because GameEngine.StartGame needs it for the
        /// first turn — the one turn no move causes.
        /// </summary>
        internal void BeginTurn(GameState state, List<GameEvent> events)
        {
            events.Add(new TurnStarted(state.GetCurrentPlayer(), state.TurnNumber));

            effects.DrawCard(state.GetCurrentPlayerEntity(), events);
        }

        private void EndTurn(GameState state, List<GameEvent> events)
        {
            events.Add(new TurnEnded(state.GetCurrentPlayer(), state.TurnNumber));

            state.AdvanceTurn();

            BeginTurn(state, events);
        }
    }
}
