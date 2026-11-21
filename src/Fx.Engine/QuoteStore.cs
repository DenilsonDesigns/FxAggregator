using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fx.Domain;

namespace Fx.Engine;

public sealed class QuoteStore : IDisposable
{
    private readonly ConcurrentDictionary<string, AtomicQuoteSlot> _slots = new(StringComparer.Ordinal);

    // Maps a currency node (e.g., "EUR") to an atomically swapped array of active connected pairs
    private readonly ConcurrentDictionary<string, ImmutablePairList> _adjacencyList = new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _sweeperCts = new();
    private Task? _sweeperTask;

    public void StartSweeper(TimeSpan maxAge, TimeSpan interval)
    {
        if (_sweeperTask != null)
            return;

        long maxAgeTicks = (long)(maxAge.TotalSeconds * Stopwatch.Frequency);
        _sweeperTask = RunSweeperAsync(maxAgeTicks, interval, _sweeperCts.Token);
    }

    private async Task RunSweeperAsync(long maxAgeTicks, TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                SweepExpiredSlots(maxAgeTicks);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected shutdown path
        }
    }

    public int SweepExpiredSlots(long maxAgeTicks)
    {
        int cleared = 0;
        long nowTicks = Stopwatch.GetTimestamp();

        foreach (var slot in _slots.Values)
        {
            var snapshot = slot.Current;
            if (snapshot is null || snapshot.TimestampTicks <= 0)
                continue;

            if (nowTicks - snapshot.TimestampTicks > maxAgeTicks && slot.TryClear(snapshot))
            {
                cleared++;
            }
        }

        return cleared;
    }

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

    public void Dispose()
    {
        _sweeperCts.Cancel();
        _sweeperCts.Dispose();
    }
}
