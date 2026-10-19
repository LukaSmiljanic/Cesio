namespace Shared.Kernel;

/// <summary>
/// Serbian bank account number: 3-digit bank code, 13-digit account, 2-digit mod 97 control number (18 digits).
/// Accepts 18 plain digits or the dashed form, where the middle part may be shortened ("160-5100-35").
/// </summary>
public sealed record SerbianBankAccountNumber
{
    private const int BankLength = 3;
    private const int AccountLength = 13;
    private const int ControlLength = 2;
    private const int Length = BankLength + AccountLength + ControlLength;
    private const int MaxInputLength = Length + 2;
    private const int VisibleAccountDigits = 4;

    private SerbianBankAccountNumber(string value) => Value = value;

    /// <summary>The 18 digits without separators.</summary>
    public string Value { get; }

    public string BankCode => Value[..BankLength];

    public static bool TryParse(string? value, [NotNullWhen(true)] out SerbianBankAccountNumber? accountNumber)
    {
        accountNumber = null;
        if (value is null || value.Length > MaxInputLength)
        {
            return false;
        }

        var digits = value.Contains('-', StringComparison.Ordinal) ? Normalize(value.Split('-')) : value;
        if (!Digits.IsAsciiDigits(digits, Length) || Mod97(digits) != 1)
        {
            return false;
        }

        accountNumber = new SerbianBankAccountNumber(digits);
        return true;
    }

    public static SerbianBankAccountNumber Parse(string? value) =>
        TryParse(value, out var accountNumber)
            ? accountNumber
            : throw new FormatException("A bank account number has 3 + 13 + 2 digits and a valid control number.");

    /// <summary>Full dashed form with the 13-digit middle part, e.g. "160-0000000005100-05". Never log it.</summary>
    public string ToDashedString() =>
        $"{BankCode}-{Value[BankLength..(BankLength + AccountLength)]}-{Value[(BankLength + AccountLength)..]}";

    /// <summary>Masked form for logs and messages (security.md §8): bank code and last four account digits, e.g. "160-*********5100-**".</summary>
    public override string ToString() =>
        $"{BankCode}-{new string('*', AccountLength - VisibleAccountDigits)}{Value[(BankLength + AccountLength - VisibleAccountDigits)..(BankLength + AccountLength)]}-**";

    private static string? Normalize(string[] parts)
    {
        if (parts.Length != 3
            || !Digits.IsAsciiDigits(parts[0], BankLength)
            || !Digits.IsAsciiDigits(parts[2], ControlLength)
            || parts[1].Length is 0 or > AccountLength
            || !Digits.IsAsciiDigits(parts[1], parts[1].Length))
        {
            return null;
        }

        return parts[0] + parts[1].PadLeft(AccountLength, '0') + parts[2];
    }

    /// <summary>The control number makes the whole 18-digit number ≡ 1 (mod 97).</summary>
    private static int Mod97(string digits)
    {
        var remainder = 0;
        foreach (var digit in digits)
        {
            remainder = (remainder * 10 + digit - '0') % 97;
        }

        return remainder;
    }
}
