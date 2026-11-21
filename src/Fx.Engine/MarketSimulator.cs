using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Fx.Domain;

namespace Fx.Engine;

public sealed class MarketSimulator : IAsyncDisposable
{
    private readonly ChannelWriter<RawTick> _writer;
    private readonly TimeSpan _tickInterval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _producerTask;
    private long _producedCount;

    private readonly SimulatedPair[] _pairs =
    [
        new(new CurrencyPair("EUR", "USD"), 1.0850m, 0.0002m),
        new(new CurrencyPair("USD", "JPY"), 154.20m, 0.03m),
        new(new CurrencyPair("GBP", "USD"), 1.2710m, 0.0003m),
        new(new CurrencyPair("EUR", "GBP"), 0.8536m, 0.0002m),
        new(new CurrencyPair("USD", "CHF"), 0.8810m, 0.0002m)
    ];

    public MarketSimulator(ChannelWriter<RawTick> writer, TimeSpan? tickInterval = null)
    {
        _writer = writer;
        _tickInterval = tickInterval ?? TimeSpan.FromMilliseconds(25);
    }

    public long ProducedCount => Volatile.Read(ref _producedCount);

    public void Start()
    {
        if (_producerTask != null)
            return;

        _producerTask = Task.Run(() => ProduceAsync(_cts.Token));
    }

    private async Task ProduceAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_tickInterval);
        var rng = Random.Shared;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                int index = rng.Next(_pairs.Length);
                ref var state = ref _pairs[index];

                // Small random walk step around current mid-price
                decimal step = (decimal)(rng.NextDouble() - 0.5) * state.Spread * 2m;
                state.Mid = Math.Max(0.0001m, state.Mid + step);

                // Occasionally widen the spread to exercise the spread guard
                decimal spread = rng.NextDouble() < 0.08
                    ? state.Spread * 8m
                    : state.Spread;

                decimal halfSpread = spread / 2m;
                decimal bid = Math.Round(state.Mid - halfSpread, 5);
                decimal ask = Math.Round(state.Mid + halfSpread, 5);
                if (ask < bid) ask = bid;

                var tick = new RawTick(state.Pair, bid, ask, Stopwatch.GetTimestamp());
                if (_writer.TryWrite(tick))
                {
                    Interlocked.Increment(ref _producedCount);
                }
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

        if (_producerTask != null)
        {
            try
            {
                await _producerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation on teardown
            }
        }

        _cts.Dispose();
    }

    private struct SimulatedPair(CurrencyPair pair, decimal mid, decimal spread)
    {
        public CurrencyPair Pair = pair;
        public decimal Mid = mid;
        public decimal Spread = spread;
    }
}