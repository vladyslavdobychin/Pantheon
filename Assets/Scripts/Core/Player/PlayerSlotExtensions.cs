using System;

namespace Pantheon.Core
{
    public static class PlayerSlotExtensions
    {
        public static PlayerSlot Opponent(this PlayerSlot slot)
        {
            return slot switch
            {
                PlayerSlot.First => PlayerSlot.Second,
                PlayerSlot.Second => PlayerSlot.First,
                _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown player slot.")
            };
        }
    }
}
