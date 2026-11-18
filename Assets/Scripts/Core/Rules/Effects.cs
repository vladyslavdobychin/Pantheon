using System;
using System.Collections.Generic;

namespace Pantheon.Core
{
    /// <summary>
    /// Atomic state mutations — the bottom layer of the rules stack.
    /// A turn transition calls these, a move calls these, and card text will call these
    /// ("Draw a card", "Deal 2 damage"). Nothing here decides whether something is *allowed*
    /// to happen: that is MoveGenerator's job.
    ///
    /// Stateless, so one instance serves every clone an AI search spawns.
    ///
    /// Events are appended to the caller's list rather than returned, so a cascade
    /// (end turn -> begin turn -> draw -> fatigue) builds one flat log with no allocation per step.
    /// </summary>
    public class Effects
    {
        /// <summary>
        /// Draws the top card. An empty deck fatigues instead; a full hand burns the card.
        /// </summary>
        public void DrawCard(Player player, List<GameEvent> events)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            if (events == null)
                throw new ArgumentNullException(nameof(events));

            if (player.Deck.IsEmpty)
            {
                DealFatigue(player, events);
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

        /// <summary>
        /// Escalating self-damage for drawing from an empty deck (design.md), which is what
        /// guarantees every game terminates.
        /// </summary>
        public void DealFatigue(Player player, List<GameEvent> events)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            if (events == null)
                throw new ArgumentNullException(nameof(events));

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
    }
}
