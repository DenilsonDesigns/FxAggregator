using System.Threading;
using Fx.Domain;

namespace Fx.Engine
{
    public sealed class AtomicQuoteSlot
    {
        private FxQuote? _currentQuote;

        public FxQuote? Current => Volatile.Read(ref _currentQuote);

        public void Update(FxQuote newQuote)
        {
            Interlocked.Exchange(ref _currentQuote, newQuote);
        }

        public bool TryClear(FxQuote expected)
        {
            return Interlocked.CompareExchange(ref _currentQuote, null, expected) == expected;
        }
    }
}