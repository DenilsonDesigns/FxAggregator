using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fx.Domain;

namespace Fx.Engine;

public sealed class QuoteStore
{
    private readonly ConcurrentDictionary<string, AtomicQuoteSlot> _slots = new(StringComparer.Ordinal);

    // Maps a currency node (e.g., "EUR") to an atomically swapped array of active connected pairs
    private readonly ConcurrentDictionary<string, ImmutablePairList> _adjacencyList = new(StringComparer.Ordinal);

    public void Upsert(FxQuote quote, CurrencyPair pair)
    {
        string pairKey = pair.ToString();
        var slot = _slots.GetOrAdd(pairKey, _ => new AtomicQuoteSlot());
        slot.Update(quote);
        AddAdjacency(pair.Base, pairKey);

        // Automatically derive and store the inverse
        var inversePair = pair.Invert();
        string inverseKey = inversePair.ToString();
        var inverseQuote = quote.CreateInverse(inverseKey);

        var inverseSlot = _slots.GetOrAdd(inverseKey, _ => new AtomicQuoteSlot());
        inverseSlot.Update(inverseQuote);
        AddAdjacency(inversePair.Base, inverseKey);
    }

    public bool TryGetQuote(string pairKey, out FxQuote? quote)
    {
        if (_slots.TryGetValue(pairKey, out var slot))
        {
            quote = slot.Current;
            return quote != null && quote.TimestampTicks > 0;
        }

        quote = default;
        return false;
    }

    public ReadOnlySpan<string> GetConnectedPairs(string currency)
    {
        if (_adjacencyList.TryGetValue(currency, out var wrapper))
        {
            return wrapper.Pairs;
        }
        return ReadOnlySpan<string>.Empty;
    }

    private void AddAdjacency(string currencyNode, string pairKey)
    {
        _adjacencyList.AddOrUpdate(
            currencyNode,
            _ => new ImmutablePairList(new[] { pairKey }),
            (_, existing) => existing.Add(pairKey)
        );
    }

    // Helper wrapper to achieve atomic reference swapping for the adjacency array without locks
    private sealed class ImmutablePairList
    {
        public string[] Pairs { get; }

        public ImmutablePairList(string[] pairs) => Pairs = pairs;

        public ImmutablePairList Add(string newPair)
        {
            if (Pairs.Contains(newPair, StringComparer.Ordinal))
                return this;

            var updated = new string[Pairs.Length + 1];
            Array.Copy(Pairs, updated, Pairs.Length);
            updated[Pairs.Length] = newPair;
            return new ImmutablePairList(updated);
        }
    }
}
