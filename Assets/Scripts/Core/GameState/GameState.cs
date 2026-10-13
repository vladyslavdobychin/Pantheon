using System;
using System.Collections.Generic;

namespace Pantheon.Core
{
    public class GameState
    {
        private readonly Player[] players = new Player[2];
        private readonly Rng rng;
        private readonly PlayerSlot currentPlayer;

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

            rng = new Rng(seed);
            currentPlayer = (PlayerSlot) rng.Next(2);

            /*
            [X] Initialize player indexes
            [X] Initialize Players with decks
            [X] Initialize board
            */
        }

        public bool HasWinner()
        {
            // If either player has HP = 0
            return false;
        }

        public PlayerSlot GetCurrentPlayer()
        {
            return currentPlayer;
        }

        public List<GameEvent> ApplyMove(IMove move)
        {
            return new List<GameEvent>();
        }
    }
}
