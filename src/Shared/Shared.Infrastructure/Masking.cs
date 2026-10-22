using Shared.Kernel;

namespace Shared.Infrastructure;

/// <summary>Masks sensitive values for audit <c>changes</c> and logs (audit.md §2, security.md §8).</summary>
public static class Masking
{
    /// <summary>"ana.petrovic@firma.rs" → "a***@firma.rs"; a value without '@' is fully masked.</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return string.Empty;
        }

        var at = email.LastIndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }

    /// <summary>Bank code and last four account digits only (see <see cref="SerbianBankAccountNumber.ToString"/>).</summary>
    public static string AccountNumber(SerbianBankAccountNumber accountNumber)
    {
        ArgumentNullException.ThrowIfNull(accountNumber);
        return accountNumber.ToString();
    }
}
