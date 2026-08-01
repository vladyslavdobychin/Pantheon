using System;

namespace Pantheon.Core.Cards;

public readonly struct CardPrice
{
    public static readonly CardPrice Zero = new(0);

    public int Amount { get; }

    public CardPrice(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Card price cannot be negative."
            );

        Amount = amount;
    }

    public override string ToString() => Amount.ToString();
}
