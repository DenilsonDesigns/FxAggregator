using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fx.Domain;

namespace Fx.Engine;

public sealed class ExecutionReactor : IAsyncDisposable
{
    private readonly SyntheticRouteFinder _routeFinder;
    private readonly (string Base, string Quote)[] _targetCrosses;
    private readonly TimeSpan _queryInterval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _reactorTask;


    private long _queriesExecuted;
    private long _hits;
    private long _misses;
    private long _totalElapsedTicks;

    public ExecutionReactor(
        SyntheticRouteFinder routeFinder,
        (string Base, string Quote)[]? targetCrosses = null,
        TimeSpan? queryInterval = null)
    {
        _routeFinder = routeFinder;
        _targetCrosses = targetCrosses ??
        [
            ("EUR", "JPY"),
            ("GBP", "JPY"),
            ("EUR", "CHF"),
            ("GBP", "CHF")
        ];
        _queryInterval = queryInterval ?? TimeSpan.FromMilliseconds(100);
    }

    public long QueriesExecuted => Volatile.Read(ref _queriesExecuted);
    public long Hits => Volatile.Read(ref _hits);
    public long Misses => Volatile.Read(ref _misses);

    public double AverageLatencyMicroseconds
    {
        get
        {
            long count = QueriesExecuted;
            if (count == 0) return 0d;

            long ticks = Volatile.Read(ref _totalElapsedTicks);
            double seconds = (double)ticks / Stopwatch.Frequency;
            return (seconds * 1_000_000d) / count;
        }
    }

    public void Start()
    {
        if (_reactorTask != null)
            return;

        _reactorTask = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_queryInterval);
        int index = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var (baseCurr, quoteCurr) = _targetCrosses[index % _targetCrosses.Length];
                index++;

                long startTicks = Stopwatch.GetTimestamp();
                bool found = _routeFinder.TryFindSyntheticQuote(baseCurr, quoteCurr, out _, out _);
                long elapsed = Stopwatch.GetTimestamp() - startTicks;

                Interlocked.Increment(ref _queriesExecuted);
                Interlocked.Add(ref _totalElapsedTicks, elapsed);

                if (found)
                    Interlocked.Increment(ref _hits);
                else
                    Interlocked.Increment(ref _misses);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected shutdown path
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_reactorTask != null)
        {
            try
            {
                await _reactorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation on teardown
            }
        }

        _cts.Dispose();
    }
}