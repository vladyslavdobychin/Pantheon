using System;

namespace Pantheon.Core
{
    public class RNG
    {
        private readonly Random random;
        public RNG(int seed)
        {
            random = new Random(seed);
        }

        public int Next(int value)
        {
            return random.Next(value);
        }
    }
}
