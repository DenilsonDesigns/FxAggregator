using System;
using System.Threading;
using System.Threading.Tasks;
using Fx.Engine;

using var store = new QuoteStore();
store.StartSweeper(maxAge: TimeSpan.FromSeconds(2), interval: TimeSpan.FromMilliseconds(500));

await using var ingestion = new TickIngestionPipeline(store, capacity: 4096);
await using var simulator = new MarketSimulator(ingestion.Writer, tickInterval: TimeSpan.FromMilliseconds(15));

var routeFinder = new SyntheticRouteFinder(
    store,
    maxAllowedAge: TimeSpan.FromSeconds(2),
    maxCumulativeSpread: 0.01m);

await using var reactor = new ExecutionReactor(routeFinder, queryInterval: TimeSpan.FromMilliseconds(75));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

ingestion.Start();
simulator.Start();
reactor.Start();

Console.WriteLine("Running FX pipeline... Press Ctrl+C to stop.");

try
{
    using var statusTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    while (await statusTimer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
    {
        Console.WriteLine(
            $"Produced: {simulator.ProducedCount} | Ingested: {ingestion.IngestedCount} | " +
            $"Queries: {reactor.QueriesExecuted} (Hits: {reactor.Hits}, Misses: {reactor.Misses}) | " +
            $"Avg Lookup: {reactor.AverageLatencyMicroseconds:F2} us");
    }
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    // Clean exit
}