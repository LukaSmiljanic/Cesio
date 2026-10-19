namespace Shared.Kernel;

internal static class Digits
{
    /// <summary>True when <paramref name="value"/> has exactly <paramref name="length"/> characters, all ASCII digits 0–9.</summary>
    public static bool IsAsciiDigits([NotNullWhen(true)] string? value, int length)
    {
        if (value is null || value.Length != length)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
