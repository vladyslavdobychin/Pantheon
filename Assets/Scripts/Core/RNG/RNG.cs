using System;

namespace Pantheon.Core
{
    public class Rng
    {
        private readonly Random random;
        public Rng(int seed)
        {
            random = new Random(seed);
        }

        // <summary>
        // Provides a random number between `0` and `exclusiveMax - 1`
        // </summary>
        public int Next(int exclusiveMax)
        {
            return random.Next(exclusiveMax);
        }
    }
}
