using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Modules.Identity.Persistence;

namespace Modules.Identity.Passwords;

/// <summary>Whether a password is known from breaches (security.md §1).</summary>
public interface IBreachedPasswordChecker
{
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken);
}

public sealed class BreachCheckOptions
{
    /// <summary>Use the k-anonymity range API of Have I Been Pwned; when off or unreachable, the offline list is used.</summary>
    public bool UseOnlineApi { get; set; } = true;
}

/// <summary>
/// Have I Been Pwned range API: only the first five hex characters of the SHA-1 hash leave the server (k-anonymity).
/// Any failure falls back to the offline list, so registration never depends on the external service.
/// </summary>
internal sealed class HibpBreachedPasswordChecker(HttpClient http, IOptions<BreachCheckOptions> options) : IBreachedPasswordChecker
{
    public async Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (OfflineBreachedPasswords.Contains(password))
        {
            return true;
        }

        if (!options.Value.UseOnlineApi)
        {
            return false;
        }

#pragma warning disable CA5350 // SHA-1 is what the range API is keyed on; it is not used for security here.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"range/{hash[..5]}", UriKind.Relative));
            request.Headers.Add("Add-Padding", "true");
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var suffix = hash[5..];
            return body.Split('\n').Any(line =>
                line.StartsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && line.Length > suffix.Length + 1
                && line[(suffix.Length + 1)..].Trim() != "0");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}

/// <summary>A short embedded list of common passwords of at least 12 characters (the offline fallback).</summary>
internal static class OfflineBreachedPasswords
{
    private static readonly HashSet<string> Passwords = Load();

    public static bool Contains(string password) => Passwords.Contains(password.Trim());

    private static HashSet<string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Modules.Identity.Passwords.common-passwords.txt")
            ?? throw new InvalidOperationException("The embedded common-password list is missing.");
        using var reader = new StreamReader(stream);
        var passwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                passwords.Add(line.Trim());
            }
        }

        return passwords;
    }
}

/// <summary>Rejects breached passwords at registration and reset.</summary>
internal sealed class BreachedPasswordValidator(IBreachedPasswordChecker checker) : IPasswordValidator<AuthUser>
{
    public const string ErrorCode = "PasswordBreached";

    public async Task<IdentityResult> ValidateAsync(UserManager<AuthUser> manager, AuthUser user, string? password) =>
        password is not null && await checker.IsBreachedAsync(password, CancellationToken.None)
            ? IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = "This password appears in known data breaches." })
            : IdentityResult.Success;
}
