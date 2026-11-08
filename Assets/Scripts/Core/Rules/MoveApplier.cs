using System;
using System.Collections.Generic;
using System.Linq;

namespace Pantheon.Core
{
    /// <summary>
    /// The write half of the step function: validates a move, mutates state, returns fat events.
    /// Stateless — state comes in as a parameter.
    /// </summary>
    public class MoveApplier
    {
        private readonly MoveGenerator generator;
        private readonly GameRules rules;

        public MoveApplier(MoveGenerator generator, GameRules rules)
        {
            if (generator == null)
                throw new ArgumentNullException(nameof(generator));

            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            this.generator = generator;
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

            BeginTurn(state, events);

            return Record(state, events);
        }

        public List<GameEvent> Apply(GameState state, Move move)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (move == null)
                throw new ArgumentNullException(nameof(move));

            if (rules.IsOver(state))
                throw new InvalidOperationException("Cannot apply a move to a finished game.");

            // Never trust the move: a remote client can send anything.
            if (!generator.GetValidMoves(state).Contains(move))
                throw new InvalidOperationException($"Illegal move: {move}");

            var events = new List<GameEvent>();

            switch (move)
            {
                case EndTurnMove:
                    ApplyEndTurn(state, events);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(move), move, "Unhandled move type.");
            }

            var result = rules.GetResult(state);
            if (result != null)
                events.Add(new GameEnded(result));

            return Record(state, events);
        }

        private void ApplyEndTurn(GameState state, List<GameEvent> events)
        {
            events.Add(new TurnEnded(state.GetCurrentPlayer(), state.TurnNumber));

            state.AdvanceTurn();

            BeginTurn(state, events);
        }

        private void BeginTurn(GameState state, List<GameEvent> events)
        {
            events.Add(new TurnStarted(state.GetCurrentPlayer(), state.TurnNumber));

            DrawCard(state.GetCurrentPlayerEntity(), events);
        }

        private void DrawCard(Player player, List<GameEvent> events)
        {
            if (player.Deck.IsEmpty)
            {
                ApplyFatigue(player, events);
                return;
            }

            var card = player.Deck.Draw();

            // Overdraw burns the card (design.md: hand cap ~10).
            if (player.Hand.IsFull)
            {
                events.Add(new CardBurned(
                    player.Slot,
                    card.Id,
                    card.Definition.Name,
                    player.Deck.Count
                ));
                return;
            }

            player.Hand.Add(card);

            events.Add(new CardDrawn(
                player.Slot,
                card.Id,
                card.Definition.Name,
                player.Hand.Count,
                player.Deck.Count
            ));
        }

        private void ApplyFatigue(Player player, List<GameEvent> events)
        {
            player.IncreaseFatigue();

            var amount = player.FatigueCounter;
            var healthBefore = player.Hero.Health;

            player.Hero.TakeDamage(amount);

            events.Add(new FatigueDamageTaken(
                player.Slot,
                amount,
                healthBefore,
                player.Hero.Health
            ));
        }

        private static List<GameEvent> Record(GameState state, List<GameEvent> events)
        {
            foreach (var gameEvent in events)
                state.RecordEvent(gameEvent);

            return events;
        }
    }
}
