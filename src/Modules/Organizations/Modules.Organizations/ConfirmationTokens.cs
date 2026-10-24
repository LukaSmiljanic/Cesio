using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Audit.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Kernel;

namespace Modules.Organizations;

/// <summary>What a confirmation link sent to the official email confirms (task 007a requirement 6).</summary>
internal static class ConfirmationPurposes
{
    public const string Pristupnica = "Pristupnica";

    public const string PayoutAccountChange = "PayoutAccountChange";

    public const string SefKeyChange = "SefKeyChange";

    public const string MfaReset = "MfaReset";

    public static readonly string[] All = [Pristupnica, PayoutAccountChange, SefKeyChange, MfaReset];
}

/// <summary>
/// Single-use confirmation links to the organization's official email in APR (ADR-020, security.md §2). Only the
/// SHA-256 of the token is stored; the link expires after <c>confirmations.tokenHours</c> (72).
/// </summary>
internal sealed class ConfirmationTokens(
    OrgsDbContext orgs,
    IAuditWriter audit,
    IEmailSender email,
    IBusinessParameters parameters,
    IOptions<OrganizationOptions> options,
    TimeProvider timeProvider)
{
    public const string TokenHoursParameter = "confirmations.tokenHours";

    /// <summary>
    /// Adds the token and its audit event to the caller's transaction; the caller sends the returned message once it
    /// committed, so a rolled-back change sends nothing.
    /// </summary>
    public async Task<EmailMessage> IssueAsync(Organization organization, string purpose, Guid subjectId, string what, CancellationToken cancellationToken, string? fingerprint = null)
    {
        // A new request voids the earlier links for the same subject: only the latest one can be confirmed.
        var now = timeProvider.GetUtcNow();
        foreach (var earlier in await orgs.ConfirmationTokens.IgnoreQueryFilters()
                     .Where(t => t.Purpose == purpose && t.SubjectId == subjectId && t.UsedAt == null && t.ExpiresAt > now)
                     .ToListAsync(cancellationToken))
        {
            earlier.UsedAt = now;
        }

        var address = organization.OfficialEmail ?? throw new InvalidOperationException("The organization has no official email.");
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hours = await parameters.GetAsync(TokenHoursParameter, 72, cancellationToken);
        var token = new ConfirmationToken
        {
            Id = SequentialGuid.NewGuid(),
            OrgId = organization.Id,
            Purpose = purpose,
            SubjectId = subjectId,
            TokenHash = Hash(raw),
            Fingerprint = fingerprint,
            ExpiresAt = now.AddHours(hours),
        };
        orgs.ConfirmationTokens.Add(token);
        await orgs.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("Confirmation.Sent", "ConfirmationToken", token.Id, organization.Id, Reason: purpose,
            Related: [new AuditReference(purpose, subjectId)]), cancellationToken);
        var link = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/confirmations/{raw}?purpose={purpose}";
        return new EmailMessage(address, "Cesio: potrebna je potvrda",
            $"Za firmu {organization.LegalName ?? organization.Pib} na platformi Cesio zatraženo je: {what}. Ako je to tačno, potvrdite u roku od {hours} sati: {link}\nAko niste vi, ne otvarajte link i javite se podršci Cesio.");
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => email.SendAsync(message, cancellationToken);

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
