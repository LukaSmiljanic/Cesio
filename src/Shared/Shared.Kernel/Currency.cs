namespace Shared.Kernel;

/// <summary>An ISO 4217 currency from the platform's allow-list (domain-model.md §4, ADR-008).</summary>
public sealed record Currency
{
    public static readonly Currency Rsd = new("RSD", 2);
    public static readonly Currency Eur = new("EUR", 2);

    private Currency(string code, int minorUnits)
    {
        Code = code;
        MinorUnits = minorUnits;
    }

    public string Code { get; }

    /// <summary>Number of decimal places of the currency's minor unit (RSD: 2).</summary>
    public int MinorUnits { get; }

    public static bool TryFromCode(string? code, [NotNullWhen(true)] out Currency? currency)
    {
        currency = code switch
        {
            "RSD" => Rsd,
            "EUR" => Eur,
            _ => null,
        };
        return currency is not null;
    }

    public static Currency FromCode(string? code) =>
        TryFromCode(code, out var currency)
            ? currency
            : throw new ArgumentException("Unsupported currency code.", nameof(code));

    public override string ToString() => Code;
}
