using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Persistence;

namespace Modules.Organizations;

/// <summary>
/// <c>GET /confirmations/{token}?purpose=</c> (public, rate limited): the official email confirms a pending change.
/// Unknown, used or expired → 410; a purpose other than the token's → 400.
/// PayoutAccountChange tokens are issued by task 009; until then nothing is pending for them.
/// </summary>
internal sealed class ConfirmationService(
    OrgsDbContext orgs,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    PristupnicaService pristupnice,
    MfaResetService mfaResets,
    SefConnectionService sefConnections,
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
            await unitOfWork.ExecuteInTransactionAsync(
                ct => audit.WriteAsync(new AuditEntry("Confirmation.Expired", "ConfirmationToken", confirmation.Id, confirmation.OrgId, Reason: confirmation.Purpose), ct),
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
                        ConfirmationPurposes.Pristupnica => await pristupnice.ConfirmByEmailAsync(confirmation.SubjectId!.Value, ct),
                        ConfirmationPurposes.MfaReset => await mfaResets.ConfirmByEmailAsync(confirmation.SubjectId!.Value, ct),
                        ConfirmationPurposes.SefKeyChange => await sefConnections.ApplyPendingKeyAsync(confirmation.OrgId, ct),
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
