using System;
using System.Collections.Generic;
using Pantheon.Core.Cards;

namespace Pantheon.Core
{
    /// <summary>
    /// Pure game data. Answers "what is" — never "what is allowed" or "what happens".
    /// Rules live in MoveGenerator / MoveApplier / GameRules and drive it through the
    /// internal mutators below.
    /// </summary>
    public class GameState
    {
        private readonly Player[] players = new Player[2];
        private readonly Rng rng;
        private readonly List<GameEvent> eventLog = new List<GameEvent>();

        private PlayerSlot currentPlayer;
        private int turnNumber;
        private int nextCardId;

        public GameState(Player playerA, Player playerB, int seed)
        {
            if (playerA == null)
                throw new ArgumentNullException(nameof(playerA));

            if (playerB == null)
                throw new ArgumentNullException(nameof(playerB));

            if (playerA.Slot == playerB.Slot)
                throw new ArgumentException("Both players must have different slots", nameof(playerB));

            players[(int)playerA.Slot] = playerA;
            players[(int)playerB.Slot] = playerB;

            Seed = seed;
            rng = new Rng(seed);
            currentPlayer = (PlayerSlot)rng.Next(2);
            turnNumber = 1;

            foreach (var player in players)
                player.SetDeck(BuildDeck(player.DeckDefinition));
        }

        public int Seed { get; }

        public int TurnNumber => turnNumber;

        public IReadOnlyList<GameEvent> EventLog => eventLog;

        public PlayerSlot GetCurrentPlayer() => currentPlayer;

        public Player GetPlayer(PlayerSlot slot) => players[(int)slot];

        public Player GetCurrentPlayerEntity() => GetPlayer(currentPlayer);

        public Player GetOpponentOf(PlayerSlot slot) => GetPlayer(slot.Opponent());

        internal void AdvanceTurn()
        {
            currentPlayer = currentPlayer.Opponent();
            turnNumber++;
        }

        internal void RecordEvent(GameEvent gameEvent)
        {
            if (gameEvent == null)
                throw new ArgumentNullException(nameof(gameEvent));

            eventLog.Add(gameEvent);
        }

        /// <summary>
        /// Turns a blueprint into a shuffled draw pile. Lives here because the seeded Rng and the
        /// card-id counter both live here — every CardInstance id is deterministic for a given seed.
        /// </summary>
        private Deck BuildDeck(DeckDefinition definition)
        {
            var cards = new List<CardInstance>(definition.Cards.Count);

            foreach (var cardDefinition in definition.Cards)
                cards.Add(new CardInstance(nextCardId++, cardDefinition));

            Shuffle(cards);

            return new Deck(cards);
        }

        private void Shuffle(List<CardInstance> cards)
        {
            for (var i = cards.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
        }
    }
}
