using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Persistence;

namespace Modules.Organizations;

public sealed record ConfirmationRequest(string? Purpose);

/// <summary>
/// <c>POST /confirmations/{token}</c> <c>{ purpose }</c> (public, rate limited, no Idempotency-Key: the token is single use; the SPA page posts it after the user's click): the official email confirms a pending change.
/// Unknown, used or expired → 410; a purpose other than the token's → 400.
/// </summary>
internal sealed class ConfirmationService(
    OrgsDbContext orgs,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    PristupnicaService pristupnice,
    MfaResetService mfaResets,
    SefConnectionService sefConnections,
    BankAccountService bankAccounts,
    TimeProvider timeProvider)
{
    public async Task<IResult> ConfirmAsync(string token, string? purpose, CancellationToken cancellationToken)
    {
        if (purpose is null || !ConfirmationPurposes.All.Contains(purpose))
        {
            return Problems.Validation("purpose", "invalid_purpose", "Unknown confirmation purpose.");
        }

        var hash = ConfirmationTokens.Hash(token);
        var now = timeProvider.GetUtcNow();
        if (await orgs.ConfirmationTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken) is not { } confirmation || confirmation.UsedAt is not null)
        {
            return Gone();
        }

        if (confirmation.ExpiresAt <= now)
        {
            // Recorded once: the token is used up, so a repeated click is a plain 410 without another audit event.
            confirmation.UsedAt = now;
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await orgs.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("Confirmation.Expired", "ConfirmationToken", confirmation.Id, confirmation.OrgId, Reason: confirmation.Purpose), ct);
                },
                cancellationToken);
            return Gone();
        }

        if (confirmation.Purpose != purpose)
        {
            return Problems.Validation("purpose", "wrong_purpose", "This link confirms something else.");
        }

        var applied = false;
        confirmation.UsedAt = now;
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await orgs.SaveChangesAsync(ct);
                    applied = confirmation.Purpose switch
                    {
                        ConfirmationPurposes.Pristupnica => await pristupnice.ConfirmByEmailAsync(confirmation.SubjectId!.Value, confirmation.Fingerprint, ct),
                        ConfirmationPurposes.MfaReset => await mfaResets.ConfirmByEmailAsync(confirmation.SubjectId!.Value, ct),
                        ConfirmationPurposes.SefKeyChange => await sefConnections.ApplyPendingKeyAsync(confirmation.OrgId, confirmation.Fingerprint, ct),
                        ConfirmationPurposes.PayoutAccountChange => await bankAccounts.ConfirmByEmailAsync(confirmation.OrgId, confirmation.SubjectId!.Value, ct),
                        _ => false,
                    };
                    await audit.WriteAsync(new AuditEntry("Confirmation.Used", "ConfirmationToken", confirmation.Id, confirmation.OrgId,
                        Reason: confirmation.Purpose, NewState: applied ? "Applied" : "NothingPending",
                        Related: confirmation.SubjectId is { } subject ? [new AuditReference(confirmation.Purpose, subject)] : null), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Gone();
        }

        return Results.Ok(new { purpose = confirmation.Purpose, confirmed = applied });
    }

    private static IResult Gone() =>
        Problems.Error(StatusCodes.Status410Gone, "confirmation_gone", "This link is no longer valid. Request a new one.");
}
