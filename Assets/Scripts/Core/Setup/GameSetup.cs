using System;

namespace Pantheon.Core
{
    public class GameSetup
    {
        public PlayerSetup First { get; }
        public PlayerSetup Second { get; }
        public int Seed  { get; }

        public GameSetup(PlayerSetup first, PlayerSetup second, int seed)
        {
            First = first;
            Second = second;
            Seed = seed;
        }

        public PlayerSetup GetPlayerSetup(PlayerSlot slot)
        {
            return slot switch
            {
                PlayerSlot.First => First,
                PlayerSlot.Second => Second,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}
