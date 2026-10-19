namespace Shared.Kernel;

/// <summary>Company registration number (matični broj) from APR: 8 ASCII digits.</summary>
/// <remarks>TODO(task-000): add the check-digit rule once it is confirmed to apply; until then only the format is validated.</remarks>
public sealed record MaticniBroj
{
    private const int Length = 8;

    private MaticniBroj(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string? value, [NotNullWhen(true)] out MaticniBroj? maticniBroj)
    {
        maticniBroj = Digits.IsAsciiDigits(value, Length) ? new MaticniBroj(value) : null;
        return maticniBroj is not null;
    }

    public static MaticniBroj Parse(string? value) =>
        TryParse(value, out var maticniBroj) ? maticniBroj : throw new FormatException("A matični broj has 8 digits.");

    public override string ToString() => Value;
}
