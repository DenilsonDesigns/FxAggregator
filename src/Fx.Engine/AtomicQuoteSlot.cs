using System.Threading;
using Fx.Domain;

namespace Fx.Engine;

/// <summary>
/// Thread-safe, lock-free container for a single currency pair's active quote.
/// Uses atomic reference swapping to guarantee zero-latency reads.
/// </summary>
public sealed class AtomicQuoteSlot
{
    private FxQuote _currentQuote;
    public FxQuote Current => Volatile.Read(ref _currentQuote);

    public void Update(FxQuote newQuote)
    {
        Interlocked.Exchange(ref _currentQuote, newQuote);
    }
}