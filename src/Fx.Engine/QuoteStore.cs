using System.Collections.Concurrent;
using Fx.Domain;

namespace Fx.Engine;

/// <summary>
/// Manages active quotes across all currency pairs with O(1) lock-free read access.
/// </summary>
public sealed class QuoteStore
{
    private readonly ConcurrentDictionary<string, AtomicQuoteSlot> _slots = new(StringComparer.Ordinal);

    public void Upsert(FxQuote quote)
    {
        var slot = _slots.GetOrAdd(quote.CurrencyPair, _ => new AtomicQuoteSlot());
        slot.Update(quote);

        // Automatically derive and store the inverse pair to optimize pathfinding lookups
        string inversePair = GetInversePairName(quote.CurrencyPair);
        var inverseQuote = quote.CreateInverse(inversePair);

        var inverseSlot = _slots.GetOrAdd(inversePair, _ => new AtomicQuoteSlot());
        inverseSlot.Update(inverseQuote);
    }

    public bool TryGetQuote(string currencyPair, out FxQuote quote)
    {
        if (_slots.TryGetValue(currencyPair, out var slot))
        {
            quote = slot.Current;
            // Ensure we don't return an uninitialized quote struct
            return quote.TimestampTicks > 0;
        }

        quote = default;
        return false;
    }

    private static string GetInversePairName(string pair)
    {
        // Assumes standard 6-char formatting like "EURUSD" -> "USDEUR" or slash-separated parsing
        if (pair.Length == 6)
        {
            return pair[3..] + pair[..3];
        }

        var parts = pair.Split('/');
        return parts.Length == 2 ? $"{parts[1]}/{parts[0]}" : pair;
    }
}