using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Fx.Domain;

namespace Fx.Engine;

public sealed class TickIngestionPipeline : IAsyncDisposable
{
    private readonly QuoteStore _store;
    private readonly Channel<RawTick> _channel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _readerTask;
    private long _ingestedCount;

    public TickIngestionPipeline(QuoteStore store, int capacity = 4096)
    {
        _store = store;

        var options = new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        };

        _channel = Channel.CreateBounded<RawTick>(options);
    }

    public ChannelWriter<RawTick> Writer => _channel.Writer;

    public long IngestedCount => Volatile.Read(ref _ingestedCount);

    public void Start()
    {
        if (_readerTask != null)
            return;

        _readerTask = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var tick in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var quote = new FxQuote(tick.Pair.ToString(), tick.Bid, tick.Ask, tick.TimestampTicks);
                _store.Upsert(quote, tick.Pair);
                Interlocked.Increment(ref _ingestedCount);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Clean shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_readerTask != null)
        {
            try
            {
                await _readerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation on teardown
            }
        }

        _cts.Dispose();
    }

}