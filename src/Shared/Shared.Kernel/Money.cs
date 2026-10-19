namespace Shared.Kernel;

/// <summary>
/// A decimal amount with its currency (ADR-008). Arithmetic never rounds; rounding to minor units happens only
/// in <see cref="Round"/>. The amount may be negative (adjustments); entities enforce their own sign rules.
/// </summary>
public sealed record Money : IComparable<Money>
{
    /// <summary>decimal(19,4) holds at most 15 integer digits (database.md).</summary>
    private const int MaxIntegerDigits = 15;

    /// <summary>Longest accepted amount string; anything longer is rejected before parsing.</summary>
    private const int MaxAmountLength = 32;

    public Money(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public static Money Zero(Currency currency) => new(0m, currency);

    public Money Add(Money other) => new(Amount + SameCurrency(other).Amount, Currency);

    public Money Subtract(Money other) => new(Amount - SameCurrency(other).Amount, Currency);

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    /// <summary>
    /// Rounds half away from zero to the currency's minor units (0.005 → 0.01, −0.005 → −0.01). The result always has
    /// exactly that scale (97000 → 97000.00): adding a zero with the minor-unit scale widens a smaller scale.
    /// </summary>
    public Money Round() =>
        new(decimal.Round(Amount, Currency.MinorUnits, MidpointRounding.AwayFromZero) + new decimal(0, 0, 0, false, (byte)Currency.MinorUnits), Currency);

    public int CompareTo(Money? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Amount.CompareTo(SameCurrency(other).Amount);
    }

    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    /// <summary>
    /// Parses an API amount string (api.md §1): ASCII digits, an optional leading minus and at most as many decimals
    /// as the currency's minor units. Exponents, separators, whitespace and non-ASCII digits are rejected.
    /// </summary>
    public static bool TryParse(string? amount, Currency currency, [NotNullWhen(true)] out Money? money)
    {
        ArgumentNullException.ThrowIfNull(currency);
        money = null;
        if (!IsWellFormedAmount(amount, currency.MinorUnits))
        {
            return false;
        }

        money = new Money(decimal.Parse(amount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), currency);
        return true;
    }

    public static Money Parse(string? amount, Currency currency) =>
        TryParse(amount, currency, out var money)
            ? money
            : throw new FormatException("The amount must be a decimal string with at most the currency's minor units.");

    /// <summary>The amount in invariant culture with its scale preserved ("97000.00" stays "97000.00").</summary>
    public string FormatAmount() => Amount.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => $"{FormatAmount()} {Currency.Code}";

    private static int Compare(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right);
    }

    private Money SameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.Currency == Currency ? other : throw new CurrencyMismatchException(Currency, other.Currency);
    }

    private static bool IsWellFormedAmount([NotNullWhen(true)] string? amount, int maxDecimals)
    {
        if (string.IsNullOrEmpty(amount) || amount.Length > MaxAmountLength)
        {
            return false;
        }

        var position = amount[0] == '-' ? 1 : 0;
        var integerDigits = CountAsciiDigits(amount, position);
        position += integerDigits;
        if (integerDigits is 0 or > MaxIntegerDigits)
        {
            return false;
        }

        if (position == amount.Length)
        {
            return true;
        }

        if (amount[position] != '.')
        {
            return false;
        }

        var decimals = CountAsciiDigits(amount, position + 1);
        return decimals > 0 && decimals <= maxDecimals && position + 1 + decimals == amount.Length;
    }

    private static int CountAsciiDigits(string value, int start)
    {
        var end = start;
        while (end < value.Length && char.IsAsciiDigit(value[end]))
        {
            end++;
        }

        return end - start;
    }
}
