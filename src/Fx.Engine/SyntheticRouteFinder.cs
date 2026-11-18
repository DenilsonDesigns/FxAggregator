using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fx.Domain;

namespace Fx.Engine;

public sealed class SyntheticRouteFinder
{
    private readonly QuoteStore _store;
    private readonly long _maxAllowedAgeTicks;
    private const int MaxHops = 3;

    public SyntheticRouteFinder(QuoteStore store, TimeSpan maxAllowedAge)
    {
        _store = store;
        _maxAllowedAgeTicks = (long)(maxAllowedAge.TotalSeconds * Stopwatch.Frequency);
    }

    public bool TryFindSyntheticQuote(string baseCurrency, string quoteCurrency, out FxQuote? resultQuote, out int hopCount)
    {
        resultQuote = default;
        hopCount = 0;

        if (baseCurrency == quoteCurrency)
            return false;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = InitializeQueue(baseCurrency);
        visited.Add(baseCurrency);

        while (queue.Count > 0)
        {
            var currentPath = queue.Dequeue();

            if (IsTargetReached(currentPath, quoteCurrency))
            {
                if (IsPathStale(currentPath.MaxTimestampTicks))
                    continue; // Route is too old; try alternative paths

                resultQuote = BuildResultQuote(baseCurrency, quoteCurrency, currentPath);
                hopCount = currentPath.Hops;
                return true;
            }

            if (currentPath.Hops >= MaxHops)
                continue;

            ExpandNeighbors(currentPath, queue, visited);
        }

        return false;
    }

    private static Queue<PathState> InitializeQueue(string baseCurrency)
    {
        var queue = new Queue<PathState>();
        queue.Enqueue(new PathState(baseCurrency, 1.0m, 1.0m, long.MaxValue, 0));
        return queue;
    }

    private static bool IsTargetReached(PathState path, string targetCurrency)
    {
        return path.CurrentNode == targetCurrency && path.Hops > 0;
    }

    private bool IsPathStale(long maxTimestampTicks)
    {
        long currentTicks = Stopwatch.GetTimestamp();
        long oldestLegAge = currentTicks - maxTimestampTicks;
        return oldestLegAge > _maxAllowedAgeTicks;
    }

    private static FxQuote BuildResultQuote(string baseCurr, string quoteCurr, PathState path)
    {
        return new FxQuote($"{baseCurr}{quoteCurr}", path.CumulativeBid, path.CumulativeAsk, path.MaxTimestampTicks);
    }

    private void ExpandNeighbors(PathState current, Queue<PathState> queue, HashSet<string> visited)
    {
        foreach (var candidatePairKey in _store.GetConnectedPairs(current.CurrentNode))
        {
            if (_store.TryGetQuote(candidatePairKey, out var quote))
            {
                string nextNode = ExtractTargetNode(current.CurrentNode, candidatePairKey);
                if (visited.Contains(nextNode))
                    continue;

                var nextState = new PathState(
                    CurrentNode: nextNode,
                    CumulativeBid: current.CumulativeBid * quote.Bid,
                    CumulativeAsk: current.CumulativeAsk * quote.Ask,
                    MaxTimestampTicks: Math.Min(current.MaxTimestampTicks, quote.TimestampTicks),
                    Hops: current.Hops + 1
                );

                visited.Add(nextNode);
                queue.Enqueue(nextState);
            }
        }
    }

    private static string ExtractTargetNode(string sourceNode, string pairKey)
    {
        var pair = CurrencyPair.Parse(pairKey);
        return pair.Base == sourceNode ? pair.Quote : pair.Base;
    }

    private readonly record struct PathState(
        string CurrentNode,
        decimal CumulativeBid,
        decimal CumulativeAsk,
        long MaxTimestampTicks,
        int Hops
    );
}