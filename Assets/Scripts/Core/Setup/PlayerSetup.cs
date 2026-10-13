using System;
using Pantheon.Core;

namespace Pantheon.Core
{
    public class PlayerSetup
    {
        public IAgent Agent { get; }
        public Deck Deck { get; }
        public PlayerSetup(IAgent agent, Deck deck)
        {
            Agent = agent;
            Deck = deck;
        }
    }
}
