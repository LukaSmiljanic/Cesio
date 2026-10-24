using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Identity.Contracts;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Organizations;

/// <param name="OrganizationId">Needed only when the user belongs to several organizations; a pending sign-in cannot list them.</param>
public sealed record MfaResetRequestBody(Guid? OrganizationId);

internal static class MfaResetStatus
{
    public const string Requested = "Requested";

    public const string Approved = "Approved";

    /// <summary>The first of two Compliance Officers executed an email-approved reset (four-eyes).</summary>
    public const string FirstExecution = "FirstExecution";

    public const string Executed = "Executed";

    public static readonly string[] Open = [Requested, Approved, FirstExecution];
}

/// <summary>
/// MFA reset without recovery codes (security.md §1, task 007a requirement 7): the user asks after the password step;
/// the official email (seller, or a factor without another Owner) or another Owner approves; a Compliance Officer
/// executes, two different officers when only the email approved. Step-up is then blocked for 72 h.
/// </summary>
internal sealed class MfaResetService(
    OrgsDbContext orgs,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IRequestContext request,
    IMfaReset mfaReset,
    IUserDirectory users,
    IEmailSender email,
    IBusinessParameters parameters,
    ConfirmationTokens confirmations,
    TimeProvider timeProvider)
{
    public const string StepUpBlockHoursParameter = "security.stepUpBlockHoursAfterMfaReset";

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    /// <summary><c>POST /auth/mfa-reset-requests</c>: by a user who passed the password but cannot pass MFA.</summary>
    public async Task<IResult> RequestAsync(MfaResetRequestBody body, CancellationToken cancellationToken)
    {
        var memberships = await orgs.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == UserId && m.Status == MembershipStatus.Active && (body.OrganizationId == null || m.OrgId == body.OrganizationId))
            .Select(m => m.OrgId)
            .ToListAsync(cancellationToken);
        if (memberships.Count == 0)
        {
            return NotFound();
        }

        if (memberships.Count > 1)
        {
            return Problems.Validation("organizationId", "organization_required", "You belong to several organizations; contact Cesio support with the company PIB.");
        }

        if (await orgs.MfaResetRequests.AnyAsync(r => r.UserId == UserId && MfaResetStatus.Open.Contains(r.Status), cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "reset_already_requested", "An MFA reset is already in progress for this account.");
        }

        var organization = await orgs.Organizations.IgnoreQueryFilters().SingleAsync(o => o.Id == memberships[0], cancellationToken);
        var otherOwners = await OwnersAsync(organization.Id, except: UserId, cancellationToken);
        var byOwner = organization.OrgType == OrganizationType.Factor && otherOwners.Count > 0;
        if (!byOwner && organization.OfficialEmail is null)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "official_email_missing", "The company's official email is not recorded; contact Cesio support.");
        }

        var reset = new MfaResetRequest
        {
            Id = SequentialGuid.NewGuid(),
            UserId = UserId,
            OrgId = organization.Id,
            ApprovalKind = byOwner ? "Owner" : "Email",
            Status = MfaResetStatus.Requested,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        orgs.MfaResetRequests.Add(reset);
        EmailMessage? confirmation = null;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await orgs.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("User.MfaResetRequested", "MfaResetRequest", reset.Id, organization.Id,
                    NewState: MfaResetStatus.Requested, Reason: reset.ApprovalKind, Related: [new AuditReference("User", UserId)]), ct);
                if (!byOwner)
                {
                    confirmation = await confirmations.IssueAsync(organization, ConfirmationPurposes.MfaReset, reset.Id, "reset dvofaktorske prijave korisnika", ct);
                }
            },
            cancellationToken);
        if (confirmation is not null)
        {
            await confirmations.SendAsync(confirmation, cancellationToken);
        }
        else
        {
            await NotifyAsync(otherOwners, null, "Cesio: zahtev za reset dvofaktorske prijave",
                "Jedan korisnik vaše organizacije zatražio je reset dvofaktorske prijave. Odobrite ga u aplikaciji samo ako ste proverili da je zahtev njegov.", cancellationToken);
        }

        return Results.Accepted(value: new { id = reset.Id, approvalKind = reset.ApprovalKind, status = reset.Status });
    }

    /// <summary><c>GET /organizations/{id}/mfa-reset-requests</c>: other members' requests that wait for an Owner's approval.</summary>
    public async Task<IResult> ListForApprovalAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Results.Ok(new
        {
            items = await orgs.MfaResetRequests.AsNoTracking()
                .Where(r => r.OrgId == organizationId && r.ApprovalKind == "Owner" && r.Status == MfaResetStatus.Requested && r.UserId != UserId)
                .OrderBy(r => r.CreatedAt)
                .Select(r => new { id = r.Id, userId = r.UserId, status = r.Status, createdAt = r.CreatedAt })
                .ToListAsync(cancellationToken),
        });

    /// <summary><c>POST /organizations/{id}/mfa-reset-requests/{requestId}/approve</c> (step-up): another Owner approves.</summary>
    public async Task<IResult> ApproveAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken)
    {
        if (await orgs.MfaResetRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.OrgId == organizationId && r.ApprovalKind == "Owner", cancellationToken) is not { } reset)
        {
            return NotFound();
        }

        if (reset.UserId == UserId)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "four_eyes_required", "Another Owner must approve your MFA reset.");
        }

        if (reset.Status != MfaResetStatus.Requested)
        {
            return Problems.ConflictState($"The request is {reset.Status}.");
        }

        reset.Status = MfaResetStatus.Approved;
        reset.ApprovedBy = UserId;
        reset.ApprovedAt = timeProvider.GetUtcNow();
        return await SaveAsync(reset, "User.MfaResetApproved", MfaResetStatus.Requested, cancellationToken);
    }

    /// <summary>The official email confirmed the request (a <c>MfaReset</c> confirmation token); runs in the caller's transaction.</summary>
    public async Task<bool> ConfirmByEmailAsync(Guid requestId, CancellationToken cancellationToken)
    {
        if (await orgs.MfaResetRequests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken) is not { Status: MfaResetStatus.Requested } reset)
        {
            return false;
        }

        reset.Status = MfaResetStatus.Approved;
        reset.ApprovedAt = timeProvider.GetUtcNow();
        await orgs.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("User.MfaResetApproved", "MfaResetRequest", reset.Id, reset.OrgId,
            PreviousState: MfaResetStatus.Requested, NewState: MfaResetStatus.Approved, Reason: "official_email_confirmed"), cancellationToken);
        return true;
    }

    /// <summary><c>GET /admin/mfa-resets</c>: requests waiting for execution.</summary>
    public async Task<IResult> QueueAsync(CancellationToken cancellationToken) =>
        Results.Ok(new
        {
            items = await orgs.MfaResetRequests.AsNoTracking()
                .Where(r => MfaResetStatus.Open.Contains(r.Status))
                .OrderBy(r => r.CreatedAt)
                .Select(r => new { id = r.Id, userId = r.UserId, organizationId = r.OrgId, approvalKind = r.ApprovalKind, status = r.Status, approvedAt = r.ApprovedAt, firstOfficer = r.FirstOfficer, createdAt = r.CreatedAt })
                .Take(200)
                .ToListAsync(cancellationToken),
        });

    /// <summary>
    /// <c>POST /admin/mfa-resets/{id}/execute</c> (Compliance Officer, step-up). An email-only approval needs two
    /// different officers; the reset then blocks step-up and notifies the security recipients.
    /// </summary>
    public async Task<IResult> ExecuteAsync(Guid requestId, CancellationToken cancellationToken)
    {
        if (await orgs.MfaResetRequests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken) is not { } reset)
        {
            return NotFound();
        }

        if (reset.Status is not (MfaResetStatus.Approved or MfaResetStatus.FirstExecution))
        {
            return Problems.ConflictState($"The request is {reset.Status}; it must be approved first.");
        }

        if (reset.UserId == UserId)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "four_eyes_required", "An officer cannot execute their own MFA reset.");
        }

        var previous = reset.Status;
        if (reset.ApprovalKind == "Email" && reset.Status == MfaResetStatus.Approved)
        {
            reset.Status = MfaResetStatus.FirstExecution;
            reset.FirstOfficer = UserId;
            return await SaveAsync(reset, "User.MfaResetFirstExecution", previous, cancellationToken);
        }

        if (reset.FirstOfficer == UserId)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "four_eyes_required", "A second, different officer must execute this reset.");
        }

        var now = timeProvider.GetUtcNow();
        var blockedUntil = now.AddHours(await parameters.GetAsync(StepUpBlockHoursParameter, 72, cancellationToken));
        reset.Status = MfaResetStatus.Executed;
        reset.ExecutedBy = UserId;
        reset.ExecutedAt = now;
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await orgs.SaveChangesAsync(ct);
                    await mfaReset.ResetAsync(reset.UserId, blockedUntil, ct);
                    await audit.WriteAsync(new AuditEntry("User.MfaResetExecuted", "MfaResetRequest", reset.Id, reset.OrgId,
                        PreviousState: previous, NewState: MfaResetStatus.Executed, Related: [new AuditReference("User", reset.UserId)]), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.ConcurrencyConflict();
        }

        var organization = await orgs.Organizations.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == reset.OrgId, cancellationToken);
        await NotifyAsync(await OwnersAsync(reset.OrgId, except: null, cancellationToken), organization.OfficialEmail, "Cesio: dvofaktorska prijava je resetovana",
            $"Dvofaktorska prijava jednog korisnika organizacije {organization.LegalName ?? organization.Pib} je resetovana. Narednih sati taj nalog ne može da odobrava osetljive radnje. Ako to niste tražili, odmah se javite podršci Cesio.",
            cancellationToken);
        return Results.Ok(new { id = reset.Id, status = reset.Status, stepUpBlockedUntil = blockedUntil });
    }

    private async Task<IResult> SaveAsync(MfaResetRequest reset, string action, string previous, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await orgs.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry(action, "MfaResetRequest", reset.Id, reset.OrgId, PreviousState: previous, NewState: reset.Status), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.ConcurrencyConflict();
        }

        return Results.Ok(new { id = reset.Id, status = reset.Status });
    }

    private Task<List<Guid>> OwnersAsync(Guid organizationId, Guid? except, CancellationToken cancellationToken) =>
        orgs.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.OrgId == organizationId && m.Status == MembershipStatus.Active && m.Role == Membership.OwnerRole && m.UserId != except)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

    /// <summary>The security recipients (security.md §2): the Owners and, when given, the official email.</summary>
    private async Task NotifyAsync(List<Guid> owners, string? officialEmail, string subject, string text, CancellationToken cancellationToken)
    {
        var addresses = new List<string>();
        foreach (var owner in owners)
        {
            if (await users.GetEmailAsync(owner, cancellationToken) is { } address)
            {
                addresses.Add(address);
            }
        }

        if (officialEmail is not null)
        {
            addresses.Add(officialEmail);
        }

        foreach (var address in addresses.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await email.SendAsync(new EmailMessage(address, subject, text), cancellationToken);
        }
    }

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
}
