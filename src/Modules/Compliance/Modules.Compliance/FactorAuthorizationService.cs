using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Compliance.Persistence;
using Modules.Documents.Contracts;
using Modules.Notifications.Contracts;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Compliance;

public sealed record FactorAuthorizationRequest(
    string? Kind, string? IssuingAuthority, string? DecisionNumber, DateOnly? DecisionDate, Guid? EvidenceDocumentId, string? OfficialSourceUrl);

public sealed record FactorAuthorizationApproval(Guid? SourceSnapshotDocumentId, string? VerificationMethod, string? Notes);

public sealed record ReasonRequest(string? Reason);

internal static class FactorAuthorizationStatus
{
    public const string Submitted = "Submitted";

    public const string PendingSecondApproval = "PendingSecondApproval";

    public const string Verified = "Verified";

    public const string Rejected = "Rejected";

    public const string Revoked = "Revoked";

    public const string Expired = "Expired";
}

/// <summary>
/// Factor authorization (task 008, compliance.md §4, INV-A2): the factor submits; a Compliance Officer checks the
/// official source (APR factoring register, NBS list of banks), attaches a snapshot and approves; a second, different
/// officer makes it Verified until <c>reverifyBy</c>. Whether the register entry alone is sufficient is open (LQ-09):
/// the officers' check is recorded, nothing more is claimed.
/// </summary>
internal sealed class FactorAuthorizationService(
    ComplianceDbContext compliance,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IRequestContext request,
    IOrganizationQuery organizations,
    IDocumentQuery documents,
    IDocumentWriter documentWriter,
    IBusinessParameters parameters,
    TimeProvider timeProvider)
{
    public const string FactoringCompanyReverifyDaysParameter = "factorAuthorization.reverifyDays.factoringCompany";

    public const string BankReverifyDaysParameter = "factorAuthorization.reverifyDays.bank";

    private static readonly string[] Methods = ["OfficialRegisterLookup", "AuthorityConfirmationLetter"];

    /// <summary>Which authority issues each kind's authorization: a data-consistency check, not a legal conclusion (LQ-09).</summary>
    private static readonly Dictionary<string, string[]> Authorities = new()
    {
        ["Bank"] = ["NationalBankOfSerbia"],
        ["FactoringCompany"] = ["MinistryOfFinance", "SecuritiesCommission"],
    };

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    /// <summary><c>GET /organizations/{id}/factor-authorizations</c>.</summary>
    public async Task<IResult> ListMineAsync(Guid orgId, CancellationToken cancellationToken) =>
        Results.Ok((await compliance.FactorAuthorizations.AsNoTracking().Where(a => a.OrgId == orgId).OrderByDescending(a => a.CreatedAt).ToListAsync(cancellationToken))
            .Select(CustomerView));

    /// <summary><c>POST /organizations/{id}/factor-authorizations</c>: a factoring company gives its decision; a bank only its kind.</summary>
    public async Task<IResult> SubmitAsync(Guid orgId, FactorAuthorizationRequest body, CancellationToken cancellationToken)
    {
        if (await organizations.GetAsync(orgId, cancellationToken) is not { Type: OrganizationType.Factor })
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only factor organizations submit an authorization.");
        }

        if (body.Kind is not { } kind || !Authorities.TryGetValue(kind, out var authorities))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Kind must be Bank or FactoringCompany.");
        }

        if (body.IssuingAuthority is not { } authority || !authorities.Contains(authority))
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "authority_inconsistent",
                $"A {kind} authorization is issued by {string.Join(" or ", authorities)}.");
        }

        var decisionNumber = body.DecisionNumber?.Trim();
        var url = body.OfficialSourceUrl?.Trim();
        var isBank = kind == "Bank";
        if (isBank && (decisionNumber is { Length: > 0 } || body.DecisionDate is not null || body.EvidenceDocumentId is not null))
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "decision_not_applicable", "A bank has no factoring decision; leave the decision fields empty.");
        }

        if (!isBank && (string.IsNullOrEmpty(decisionNumber) || decisionNumber.Length > 100 || body.DecisionDate is null || body.EvidenceDocumentId is null))
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "decision_required",
                "A factoring company gives the decision number, the decision date and the approval document.");
        }

        if (url is { Length: > 0 } && (url.Length > 1000 || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "The official register link must be an https URL.");
        }

        if (body.EvidenceDocumentId is { } evidenceId
            && (await documents.GetAsync(evidenceId, cancellationToken) is not { ScanStatus: ScanStatus.Clean } evidence || evidence.OwnerOrgId != orgId))
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "wrong_document", "The approval document must be a clean document of your organization.");
        }

        if (await compliance.FactorAuthorizations.AnyAsync(a => a.OrgId == orgId
                && (a.Status == FactorAuthorizationStatus.Submitted || a.Status == FactorAuthorizationStatus.PendingSecondApproval), cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "authorization_pending", "An authorization of your organization is already waiting for compliance.");
        }

        var authorization = new FactorAuthorization
        {
            Id = SequentialGuid.NewGuid(),
            OrgId = orgId,
            Kind = kind,
            IssuingAuthority = authority,
            DecisionNumber = isBank ? null : decisionNumber,
            DecisionDate = body.DecisionDate,
            EvidenceDocumentId = body.EvidenceDocumentId,
            OfficialSourceUrl = string.IsNullOrEmpty(url) ? null : url,
            Status = FactorAuthorizationStatus.Submitted,
            SubmittedBy = UserId,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        compliance.FactorAuthorizations.Add(authorization);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await compliance.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("FactorAuthorization.Submitted", "FactorAuthorization", authorization.Id, orgId,
                    NewState: authorization.Status, Changes: new Dictionary<string, string?> { ["kind"] = kind, ["issuingAuthority"] = authority },
                    EvidenceDocumentIds: authorization.EvidenceDocumentId is { } doc ? [doc] : null), ct);
            },
            cancellationToken);
        return Results.Created($"/api/v1/organizations/{orgId}/factor-authorizations/{authorization.Id}", CustomerView(authorization));
    }

    /// <summary><c>GET /admin/factor-authorizations?status=</c>.</summary>
    public async Task<IResult> QueueAsync(string? status, CancellationToken cancellationToken)
    {
        var statuses = status is null ? [FactorAuthorizationStatus.Submitted, FactorAuthorizationStatus.PendingSecondApproval] : new[] { status };
        var rows = await compliance.FactorAuthorizations.AsNoTracking().Where(a => statuses.Contains(a.Status)).OrderBy(a => a.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var result = new List<object>();
        foreach (var row in rows)
        {
            var organization = await organizations.GetAsync(row.OrgId, cancellationToken);
            result.Add(AdminView(row, organization));
        }

        return Results.Ok(result);
    }

    /// <summary><c>POST /admin/factor-authorizations/{id}/snapshot</c>: the officer's print of the official source, stored as the factor's KYB evidence.</summary>
    public async Task<IResult> UploadSnapshotAsync(Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (await compliance.FactorAuthorizations.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken) is not { } authorization)
        {
            return NotFound();
        }

        if (file is null || file.Length is 0 or > 10 * 1024 * 1024)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Upload the snapshot (PDF or image, at most 10 MB).");
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        if (await documentWriter.StoreAsync(authorization.OrgId, DocumentPurpose.KybEvidence, Path.GetFileName(file.FileName), content, cancellationToken) is not { } stored)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "unsupported_file_type", "The snapshot must be a PDF or an image.");
        }

        return Results.Created($"/api/v1/documents/{stored.Id}", new { documentId = stored.Id });
    }

    /// <summary>
    /// <c>POST /admin/factor-authorizations/{id}/approve</c> (step-up): the first officer records the method, snapshot and
    /// notes; a second, different officer makes it Verified, and a previous Verified authorization of the factor expires.
    /// </summary>
    public async Task<IResult> ApproveAsync(Guid id, FactorAuthorizationApproval body, CancellationToken cancellationToken)
    {
        if (await compliance.FactorAuthorizations.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) is not { } authorization)
        {
            return NotFound();
        }

        var previous = authorization.Status;
        if (authorization.Status == FactorAuthorizationStatus.Submitted)
        {
            var notes = body.Notes?.Trim();
            if (body.VerificationMethod is not { } method || !Methods.Contains(method) || body.SourceSnapshotDocumentId is not { } snapshotId || notes?.Length > 1000)
            {
                return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed",
                    "Give the verification method (OfficialRegisterLookup or AuthorityConfirmationLetter), the source snapshot and notes of at most 1000 characters.");
            }

            if (await documents.GetAsync(snapshotId, cancellationToken) is not { ScanStatus: ScanStatus.Clean, Purpose: DocumentPurpose.KybEvidence } snapshot || snapshot.OwnerOrgId != authorization.OrgId)
            {
                return Problems.Error(StatusCodes.Status422UnprocessableEntity, "wrong_document", "Upload the snapshot for this authorization first.");
            }

            authorization.SourceSnapshotDocumentId = snapshotId;
            authorization.VerificationMethod = method;
            authorization.Notes = string.IsNullOrEmpty(notes) ? null : notes;
            authorization.FirstApprover = UserId;
            authorization.Status = FactorAuthorizationStatus.PendingSecondApproval;
            return await SaveAsync(authorization, "FactorAuthorization.FirstApproval", previous, null, null, cancellationToken);
        }

        if (authorization.Status != FactorAuthorizationStatus.PendingSecondApproval)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", $"The authorization is {authorization.Status}.");
        }

        if (authorization.FirstApprover == UserId)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "four_eyes_required", "A second, different officer must approve the authorization.");
        }

        var days = await parameters.GetAsync(authorization.Kind == "Bank" ? BankReverifyDaysParameter : FactoringCompanyReverifyDaysParameter,
            authorization.Kind == "Bank" ? 30 : 90, cancellationToken);
        var now = timeProvider.GetUtcNow();
        authorization.Status = FactorAuthorizationStatus.Verified;
        authorization.VerifiedBy = UserId;
        authorization.VerifiedAt = now;
        authorization.ReverifyBy = BusinessDayCalendar.Today(timeProvider).AddDays(days);
        var superseded = await compliance.FactorAuthorizations
            .Where(a => a.OrgId == authorization.OrgId && a.Status == FactorAuthorizationStatus.Verified && a.Id != authorization.Id)
            .ToListAsync(cancellationToken);
        foreach (var old in superseded)
        {
            old.Status = FactorAuthorizationStatus.Expired;
            old.DecidedAt = now;
            old.DecisionReason = "superseded";
        }

        // Two saves in order: the old authorization leaves Verified first (`more` runs before the main save with the
        // new one still in its previous status), so the two never meet in ux_factor_auth_active.
        authorization.Status = previous;
        return await SaveAsync(authorization, "FactorAuthorization.Verified", previous, null, async ct =>
        {
            await compliance.SaveChangesAsync(ct);
            foreach (var old in superseded)
            {
                await audit.WriteAsync(new AuditEntry("FactorAuthorization.Expired", "FactorAuthorization", old.Id, old.OrgId,
                    PreviousState: FactorAuthorizationStatus.Verified, NewState: FactorAuthorizationStatus.Expired, Reason: "superseded",
                    Related: [new AuditReference("FactorAuthorization", authorization.Id)]), ct);
            }

            authorization.Status = FactorAuthorizationStatus.Verified;
        }, cancellationToken, beforeSave: true);
    }

    /// <summary><c>POST /admin/factor-authorizations/{id}/reject</c> (step-up), before it is Verified.</summary>
    public async Task<IResult> RejectAsync(Guid id, ReasonRequest body, CancellationToken cancellationToken) =>
        await DecideAsync(id, body, [FactorAuthorizationStatus.Submitted, FactorAuthorizationStatus.PendingSecondApproval], FactorAuthorizationStatus.Rejected, cancellationToken);

    /// <summary><c>POST /admin/factor-authorizations/{id}/revoke</c> (step-up): immediate; INV-A2 consequences follow the event.</summary>
    public async Task<IResult> RevokeAsync(Guid id, ReasonRequest body, CancellationToken cancellationToken) =>
        await DecideAsync(id, body, [FactorAuthorizationStatus.Verified], FactorAuthorizationStatus.Revoked, cancellationToken);

    /// <summary>
    /// Daily (task 020): reminders to the factor 14 and 7 days (factoring company) or 7 and 2 days (bank) before
    /// <c>reverifyBy</c> (product-spec §8), and a re-check alert to compliance 14 / 7 days before (compliance.md §4).
    /// </summary>
    public async Task<int> RemindReverificationAsync(CancellationToken cancellationToken)
    {
        var today = BusinessDayCalendar.Today(timeProvider);
        var verified = await compliance.FactorAuthorizations.AsNoTracking()
            .Where(a => a.Status == FactorAuthorizationStatus.Verified && a.ReverifyBy != null && a.ReverifyBy <= today.AddDays(14))
            .ToListAsync(cancellationToken);
        var count = 0;
        foreach (var authorization in verified)
        {
            var left = authorization.ReverifyBy!.Value.DayNumber - today.DayNumber;
            int[] thresholds = authorization.Kind == "Bank" ? [7, 2] : [14, 7];
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    // The smallest threshold reached: a first run five days before sends one reminder, not two.
                    if (thresholds.Where(t => left <= t).DefaultIfEmpty().Min() is var threshold and > 0)
                    {
                        await outbox.AddAsync(new OrganizationNotificationRequested(authorization.OrgId, NotificationTypes.Reminder, $"/organizations/{authorization.OrgId}", null,
                            $"authorization-reverify:{authorization.Id}:{threshold}"), ct);
                    }

                    if (left == thresholds[0])
                    {
                        await audit.WriteAsync(new AuditEntry("Compliance.Alert", "FactorAuthorization", authorization.Id, authorization.OrgId, Reason: "reverification_due"), ct);
                    }
                },
                cancellationToken);
            count++;
        }

        return count;
    }

    /// <summary>The daily expiry (compliance.md §4 item 4): Verified authorizations past <c>reverifyBy</c> become Expired.</summary>
    /// <returns>How many expired.</returns>
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var today = BusinessDayCalendar.Today(timeProvider);
        var due = await compliance.FactorAuthorizations
            .Where(a => a.Status == FactorAuthorizationStatus.Verified && a.ReverifyBy < today)
            .Take(500)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var authorization in due)
        {
            authorization.Status = FactorAuthorizationStatus.Expired;
            authorization.DecidedAt = now;
            authorization.DecisionReason = "reverification_due";
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await compliance.SaveChangesAsync(ct);
                foreach (var authorization in due)
                {
                    await audit.WriteAsync(new AuditEntry("FactorAuthorization.Expired", "FactorAuthorization", authorization.Id, authorization.OrgId,
                        PreviousState: FactorAuthorizationStatus.Verified, NewState: FactorAuthorizationStatus.Expired, Reason: "reverification_due"), ct);
                    await outbox.AddAsync(new FactorAuthorizationExpired(authorization.OrgId, authorization.Id), ct);
                }
            },
            cancellationToken);
        return due.Count;
    }

    private async Task<IResult> DecideAsync(Guid id, ReasonRequest body, string[] from, string to, CancellationToken cancellationToken)
    {
        var reason = body.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 1000)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "A reason (at most 1000 characters) is required.");
        }

        if (await compliance.FactorAuthorizations.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) is not { } authorization)
        {
            return NotFound();
        }

        if (!from.Contains(authorization.Status))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", $"The authorization is {authorization.Status}.");
        }

        var previous = authorization.Status;
        authorization.Status = to;
        authorization.DecisionReason = reason;
        authorization.DecidedBy = UserId;
        authorization.DecidedAt = timeProvider.GetUtcNow();
        return await SaveAsync(authorization, $"FactorAuthorization.{to}", previous, reason,
            to == FactorAuthorizationStatus.Revoked ? ct => outbox.AddAsync(new FactorAuthorizationRevoked(authorization.OrgId, authorization.Id), ct) : null,
            cancellationToken);
    }

    /// <param name="more">Runs after the save, or before it when <paramref name="beforeSave"/>.</param>
    private async Task<IResult> SaveAsync(
        FactorAuthorization authorization, string action, string previous, string? reason, Func<CancellationToken, Task>? more, CancellationToken cancellationToken, bool beforeSave = false)
    {
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    if (beforeSave && more is not null)
                    {
                        await more(ct);
                    }

                    await compliance.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry(action, "FactorAuthorization", authorization.Id, authorization.OrgId,
                        PreviousState: previous, NewState: authorization.Status, Reason: reason,
                        Changes: new Dictionary<string, string?>
                        {
                            ["verificationMethod"] = authorization.VerificationMethod,
                            ["firstApprover"] = authorization.FirstApprover?.ToString(),
                            ["verifiedBy"] = authorization.VerifiedBy?.ToString(),
                            ["reverifyBy"] = authorization.ReverifyBy?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                        },
                        EvidenceDocumentIds: new[] { authorization.EvidenceDocumentId, authorization.SourceSnapshotDocumentId }.OfType<Guid>().ToList()), ct);
                    if (!beforeSave && more is not null)
                    {
                        await more(ct);
                    }
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Error(StatusCodes.Status412PreconditionFailed, "concurrency_conflict", "The authorization was changed by someone else. Reload it.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // ux_factor_auth_active: another authorization of the factor was verified at the same time.
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Another authorization of this factor was verified at the same time.");
        }

        return Results.Ok(AdminView(authorization, null));
    }

    private static object CustomerView(FactorAuthorization a) => new
    {
        id = a.Id,
        kind = a.Kind,
        issuingAuthority = a.IssuingAuthority,
        decisionNumber = a.DecisionNumber,
        decisionDate = a.DecisionDate,
        evidenceDocumentId = a.EvidenceDocumentId,
        officialSourceUrl = a.OfficialSourceUrl,
        status = a.Status,
        reverifyBy = a.ReverifyBy,
        reason = a.Status is FactorAuthorizationStatus.Rejected ? a.DecisionReason : null,
        createdAt = a.CreatedAt,
    };

    private static object AdminView(FactorAuthorization a, OrganizationSummary? organization) => new
    {
        id = a.Id,
        organizationId = a.OrgId,
        legalName = organization?.LegalName,
        pib = organization?.Pib,
        kind = a.Kind,
        issuingAuthority = a.IssuingAuthority,
        decisionNumber = a.DecisionNumber,
        decisionDate = a.DecisionDate,
        evidenceDocumentId = a.EvidenceDocumentId,
        officialSourceUrl = a.OfficialSourceUrl,
        sourceSnapshotDocumentId = a.SourceSnapshotDocumentId,
        verificationMethod = a.VerificationMethod,
        notes = a.Notes,
        status = a.Status,
        firstApprover = a.FirstApprover,
        verifiedBy = a.VerifiedBy,
        verifiedAt = a.VerifiedAt,
        reverifyBy = a.ReverifyBy,
        decisionReason = a.DecisionReason,
        createdAt = a.CreatedAt,
        version = a.Version,
    };

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
}

/// <summary>
/// INV-A2 (task 008): Approved factor organization, no active hold, and a Verified authorization with
/// <c>reverifyBy</c> ≥ today (Belgrade). Kept apart from <see cref="FactorAuthorizationService"/>, which needs the
/// document store, whose access policies ask this query.
/// </summary>
internal sealed class FactorAuthorizationQuery(ComplianceDbContext compliance, IOrganizationQuery organizations, IHoldQuery holds, TimeProvider timeProvider)
    : IAuthorizationQuery
{
    public async Task<bool> IsFactorAuthorizedAsync(Guid factorOrgId, CancellationToken cancellationToken)
    {
        var today = BusinessDayCalendar.Today(timeProvider);
        return await organizations.GetAsync(factorOrgId, cancellationToken) is { Type: OrganizationType.Factor, IsApproved: true }
            && !await holds.HasActiveHoldAsync(HoldSubject.Organization, factorOrgId, cancellationToken)
            && await compliance.FactorAuthorizations.AsNoTracking()
                .AnyAsync(a => a.OrgId == factorOrgId && a.Status == FactorAuthorizationStatus.Verified && a.ReverifyBy >= today, cancellationToken);
    }
}
