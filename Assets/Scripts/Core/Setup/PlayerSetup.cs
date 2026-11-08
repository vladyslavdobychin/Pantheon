using System;
using Pantheon.Core;

namespace Pantheon.Core
{
    public class PlayerSetup
    {
        public IAgent Agent { get; }
        public DeckDefinition Deck { get; }
        public PlayerSetup(IAgent agent, DeckDefinition deck)
        {
            Agent = agent;
            Deck = deck;
        }
    }
}
