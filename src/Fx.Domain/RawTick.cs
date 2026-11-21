using System;
using System.ComponentModel;

namespace Fx.Domain;

public readonly record struct RawTick(
    CurrencyPair Pair,
    decimal Bid,
    decimal Ask,
    long TimestampTicks)
{
    public RawTick Validate()
    {
        if (Bid <= 0m)
            throw new ArgumentOutOfRangeException(nameof(Bid), "Bid must be positive.");

        if (Ask < Bid)
            throw new ArgumentException("Ask cannot be less than Bid.", nameof(Ask));

        if (TimestampTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(TimestampTicks), "TimestampTicks must be positive.");

        return this;
    }
}