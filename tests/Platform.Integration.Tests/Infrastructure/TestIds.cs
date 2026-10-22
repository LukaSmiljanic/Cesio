using Shared.Kernel;

namespace Platform.Integration.Tests.Infrastructure;

/// <summary>Random Serbian identifiers with valid check digits.</summary>
public static class TestIds
{
    public static string NewPib() => WithCheckDigit(8, value => Pib.TryParse(value, out _));

    public static string NewMb() => WithCheckDigit(7, value => MaticniBroj.TryParse(value, out _));

    private static string WithCheckDigit(int length, Func<string, bool> valid)
    {
        while (true)
        {
            var prefix = string.Concat(Enumerable.Range(0, length).Select(i => (char)('0' + Random.Shared.Next(i == 0 ? 1 : 0, 10))));
            for (var digit = 0; digit < 10; digit++)
            {
                if (valid(prefix + digit))
                {
                    return prefix + digit;
                }
            }
        }
    }
}
