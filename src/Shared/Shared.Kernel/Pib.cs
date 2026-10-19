namespace Shared.Kernel;

/// <summary>Serbian tax identification number: 9 ASCII digits, the last one an ISO 7064 MOD 11,10 check digit.</summary>
public sealed record Pib
{
    private const int Length = 9;

    private Pib(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string? value, [NotNullWhen(true)] out Pib? pib)
    {
        pib = Digits.IsAsciiDigits(value, Length) && HasValidCheckDigit(value) ? new Pib(value) : null;
        return pib is not null;
    }

    public static Pib Parse(string? value) =>
        TryParse(value, out var pib) ? pib : throw new FormatException("A PIB has 9 digits and a valid check digit.");

    public override string ToString() => Value;

    private static bool HasValidCheckDigit(string value)
    {
        var product = 10;
        for (var i = 0; i < Length - 1; i++)
        {
            var sum = (value[i] - '0' + product) % 10;
            product = 2 * (sum == 0 ? 10 : sum) % 11;
        }

        return (11 - product) % 10 == value[Length - 1] - '0';
    }
}
