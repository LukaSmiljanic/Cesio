using Shared.Kernel;

namespace Platform.Integration.Tests.Infrastructure;

/// <summary>Random Serbian identifiers with valid check digits.</summary>
public static class TestIds
{
    public static string NewPib() => WithCheckDigit(8, value => Pib.TryParse(value, out _));

    public static string NewMb() => WithCheckDigit(7, value => MaticniBroj.TryParse(value, out _));

    /// <summary>An 18-digit account number with valid control digits (mod 97), bank 160 unless given.</summary>
    public static string NewBankAccount(string bankCode = "160")
    {
        var body = bankCode + string.Concat(Enumerable.Range(0, 13).Select(_ => (char)('0' + Random.Shared.Next(0, 10))));
        var control = 98 - (int)(System.Numerics.BigInteger.Parse(body + "00", System.Globalization.CultureInfo.InvariantCulture) % 97);
        return body + control.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>SQL that gives an organization a Verified payout account past its cool-down (task 009), for tests of other flows.</summary>
    public static string VerifiedAccountSql(Guid org) =>
        $"INSERT INTO orgs.bank_accounts (id, org_id, account_number, currency, bank_name, status, duplicate_flag, verified_at, usable_from, created_by, created_at, version) " +
        $"VALUES (NEWID(), '{org}', '{NewBankAccount()}', 'RSD', 'Banca Intesa', 'Verified', 0, DATEADD(day, -4, SYSDATETIMEOFFSET()), DATEADD(day, -1, SYSDATETIMEOFFSET()), NEWID(), DATEADD(day, -5, SYSDATETIMEOFFSET()), 0)";

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
