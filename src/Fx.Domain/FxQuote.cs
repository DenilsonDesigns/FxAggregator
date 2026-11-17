using System;

namespace Fx.Domain;


/// <summary>
/// Represents an immutable, high-precision foreign exchange price tick.
/// Implemented as a readonly record struct to guarantee zero heap allocation 
/// and prevent torn reads or in-place mutations.
/// </summary>
public readonly record struct FxQuote
{
    public string CurrencyPair { get; init; }
    public decimal Bid { get; init; }
    public decimal Ask { get; init; }
    public long TimestampTicks { get; init; }

    public FxQuote(string currencyPair, decimal bid, decimal ask, long timestampTicks)
    {
        if (bid <= 0 || ask <= 0)
            throw new ArgumentOutOfRangeException("Bid and Ask prices must be greater than zero.");

        if (bid >= ask)
            throw new ArgumentException("Bid price must be strictly less than Ask price.");

        CurrencyPair = currencyPair ?? throw new ArgumentNullException(nameof(currencyPair));
        Bid = bid;
        Ask = ask;
        TimestampTicks = timestampTicks;
    }

    /// <summary>
    /// Computes the deterministic inverse of the quote (e.g., converting EUR/USD to USD/EUR).
    /// Reciprocal logic: New Bid = 1 / Old Ask, New Ask = 1 / Old Bid.
    /// </summary>
    public FxQuote CreateInverse(string invertedPairName)
    {
        return new FxQuote(
            currencyPair: invertedPairName,
            bid: 1.0m / Ask,
            ask: 1.0m / Bid,
            timestampTicks: TimestampTicks
        );
    }
}