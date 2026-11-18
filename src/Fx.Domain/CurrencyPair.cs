namespace Fx.Domain;

public readonly record struct CurrencyPair(string Base, string Quote)
{
    public override string ToString() => $"{Base}/{Quote}";

    public CurrencyPair Invert() => new(Quote, Base);

    public static CurrencyPair Parse(string pairString)
    {
        if (pairString.Length == 6)
            return new CurrencyPair(pairString[..3], pairString[3..]);

        var parts = pairString.Split('/');
        if (parts.Length == 2)
            return new CurrencyPair(parts[0], parts[1]);

        throw new ArgumentException($"Invalid currency pair format: {pairString}");
    }
}