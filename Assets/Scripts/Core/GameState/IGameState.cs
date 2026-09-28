using System;
using System.Collections.Generic;

namespace Pantheon.Core
{
    public interface IGameState
    {
        public bool HasWinner();

        public int GetCurrentAgentIndex();

        // TODO: return type should be some sort of List<GameEvent>
        public List<GameEvent> ApplyMove(IMove move);
    }
}
