using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Compliance.Persistence;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Compliance;

public sealed record BeneficialOwnerInput(string? Name, DateOnly? DateOfBirth, string? Nationality, decimal? OwnershipPercent, string? ControlType, bool PepDeclared);

public sealed record BeneficialOwnersRequest(IReadOnlyList<BeneficialOwnerInput>? Owners);

public sealed record ConsentRequest(bool Consent);

public sealed record ScreeningResolutionRequest(string? Result, string? Note);

public sealed record RiskRatingRequest(string? Rating, string? Reason);

public sealed record KybDecisionRequest(string? Decision, string? Reason, bool RepresentativesVerified);

/// <summary>
/// KYB (task 007, compliance.md §1): a case per submission, beneficial owners and the data-sharing consent from the
/// organization, screening, risk rating and the decision by compliance staff, with four-eyes for high risk.
/// </summary>
internal sealed class KybService(
    ComplianceDbContext compliance,
    IScreeningService screening,
    IOrganizationQuery organizations,
    IOrganizationOnboarding onboarding,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IRequestContext request,
    TimeProvider timeProvider) : IKybQuery, IIntegrationEventConsumer<OrganizationSubmitted>
{
    public const string ConsumerName = "compliance.kyb";

    private static readonly string[] Ratings = ["Low", "Medium", "High"];

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    /// <summary>A submitted organization gets a KYB case, screened at once, and the organization is UnderReview.</summary>
    public async Task HandleAsync(OrganizationSubmitted integrationEvent, Guid eventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var orgId = integrationEvent.OrganizationId;
        if (await compliance.KybCases.AnyAsync(k => k.OrganizationId == orgId && (k.Status == "Open" || k.Status == "InReview" || k.Status == "PendingSecondApproval"), cancellationToken))
        {
            return;
        }

        var previousConsent = await compliance.KybCases.AsNoTracking().Where(k => k.OrganizationId == orgId).OrderByDescending(k => k.CreatedAt)
            .Select(k => (bool?)k.SharingConsent).FirstOrDefaultAsync(cancellationToken) ?? false;
        var kyb = new KybCase { Id = SequentialGuid.NewGuid(), OrganizationId = orgId, Status = "Open", SharingConsent = previousConsent, CreatedAt = timeProvider.GetUtcNow() };
        compliance.KybCases.Add(kyb);
        await compliance.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("KybCase.Opened", "KybCase", kyb.Id, orgId, NewState: "Open", Changes: new Dictionary<string, string?> { ["eventId"] = eventId.ToString() }), cancellationToken);
        await ScreenCaseAsync(kyb, cancellationToken);
        kyb.Status = "InReview";
        await compliance.SaveChangesAsync(cancellationToken);
        await onboarding.MoveAsync(orgId, OnboardingStatus.UnderReview, "kyb_case_opened", cancellationToken);
    }

    // ----- customer -----

    public async Task<IResult> CustomerViewAsync(Guid orgId, CancellationToken cancellationToken)
    {
        var kyb = await LatestCaseAsync(orgId, cancellationToken);
        var owners = await compliance.BeneficialOwners.AsNoTracking().Where(o => o.OrganizationId == orgId && o.Current).ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            status = kyb?.Status,
            sharingConsent = kyb?.SharingConsent ?? false,
            beneficialOwners = owners.Select(o => new { name = o.Name, dateOfBirth = o.DateOfBirth, nationality = o.Nationality, ownershipPercent = o.OwnershipPercent, controlType = o.ControlType, pepDeclared = o.PepDeclared }),
        });
    }

    /// <summary>The organization declares its beneficial owners; a new declaration replaces the current one and is screened.</summary>
    public async Task<IResult> DeclareOwnersAsync(Guid orgId, BeneficialOwnersRequest body, CancellationToken cancellationToken)
    {
        var owners = body.Owners ?? [];
        if (owners.Count is 0 or > 20 || owners.Any(o => string.IsNullOrWhiteSpace(o.Name) || o.Name.Length > 200
            || (o.OwnershipPercent is null && string.IsNullOrWhiteSpace(o.ControlType))
            || o.OwnershipPercent is < 0 or > 100
            || (o.Nationality is { } n && (n.Length != 2 || !n.All(char.IsAsciiLetterUpper)))
            || o.ControlType is { Length: > 100 }))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed",
                "Declare 1–20 beneficial owners, each with a name, a share (0–100%) or a control type, and an ISO nationality (e.g. RS).");
        }

        foreach (var current in await compliance.BeneficialOwners.Where(o => o.OrganizationId == orgId && o.Current).ToListAsync(cancellationToken))
        {
            current.Current = false;
        }

        var now = timeProvider.GetUtcNow();
        compliance.BeneficialOwners.AddRange(owners.Select(o => new BeneficialOwner
        {
            Id = SequentialGuid.NewGuid(),
            OrganizationId = orgId,
            Name = o.Name!.Trim(),
            DateOfBirth = o.DateOfBirth,
            Nationality = o.Nationality,
            OwnershipPercent = o.OwnershipPercent,
            ControlType = string.IsNullOrWhiteSpace(o.ControlType) ? null : o.ControlType.Trim(),
            PepDeclared = o.PepDeclared,
            Current = true,
            DeclaredBy = UserId,
            DeclaredAt = now,
        }));
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await compliance.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Kyb.BeneficialOwnersDeclared", "Organization", orgId, orgId,
                    Changes: new Dictionary<string, string?> { ["count"] = owners.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }), ct);
                if (await LatestCaseAsync(orgId, ct) is { Status: "InReview" } open)
                {
                    await ScreenCaseAsync(open, ct);
                    await compliance.SaveChangesAsync(ct);
                }
            },
            cancellationToken);
        return await CustomerViewAsync(orgId, cancellationToken);
    }

    /// <summary>The KYB data-sharing consent (compliance.md §1): factors of the organization's deals may receive its KYB package.</summary>
    public async Task<IResult> ConsentAsync(Guid orgId, ConsentRequest body, CancellationToken cancellationToken)
    {
        if (await compliance.KybCases.Where(k => k.OrganizationId == orgId).OrderByDescending(k => k.CreatedAt).FirstOrDefaultAsync(cancellationToken) is not { } kyb)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Submit the organization for review first.");
        }

        kyb.SharingConsent = body.Consent;
        kyb.SharingConsentAt = timeProvider.GetUtcNow();
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await compliance.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry(body.Consent ? "Kyb.SharingConsentGiven" : "Kyb.SharingConsentWithdrawn", "KybCase", kyb.Id, orgId), ct);
            },
            cancellationToken);
        return Results.Ok(new { sharingConsent = kyb.SharingConsent });
    }

    // ----- compliance staff -----

    public async Task<IResult> QueueAsync(string? status, CancellationToken cancellationToken)
    {
        var cases = await compliance.KybCases.AsNoTracking()
            .Where(k => status == null ? (k.Status == "InReview" || k.Status == "PendingSecondApproval") : k.Status == status)
            .OrderBy(k => k.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var rows = new List<object>();
        foreach (var kyb in cases)
        {
            var org = await organizations.GetAsync(kyb.OrganizationId, cancellationToken);
            rows.Add(new { id = kyb.Id, organizationId = kyb.OrganizationId, legalName = org?.LegalName, pib = org?.Pib, type = org?.Type, status = kyb.Status, riskRating = kyb.RiskRating, createdAt = kyb.CreatedAt });
        }

        return Results.Ok(rows);
    }

    public async Task<IResult> DetailAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await compliance.KybCases.AsNoTracking().SingleOrDefaultAsync(k => k.Id == id, cancellationToken) is not { } kyb)
        {
            return NotFound();
        }

        var org = await organizations.GetAsync(kyb.OrganizationId, cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var owners = await compliance.BeneficialOwners.AsNoTracking().Where(o => o.OrganizationId == kyb.OrganizationId && o.Current).ToListAsync(cancellationToken);
        var results = await compliance.ScreeningResults.AsNoTracking().Where(r => r.KybCaseId == id && r.Result != "Superseded").OrderBy(r => r.SubjectKind).ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            id = kyb.Id,
            status = kyb.Status,
            riskRating = kyb.RiskRating,
            riskReason = kyb.RiskReason,
            sharingConsent = kyb.SharingConsent,
            firstApprover = kyb.FirstApprover,
            decisionReason = kyb.DecisionReason,
            nextReviewAt = kyb.NextReviewAt,
            organization = org is null ? null : new { id = org.Id, legalName = org.LegalName, pib = org.Pib, type = org.Type, status = org.Status },
            representatives = await organizations.AuthorizedSignerNamesAsync(kyb.OrganizationId, today, cancellationToken),
            beneficialOwners = owners.Select(o => new { name = o.Name, dateOfBirth = o.DateOfBirth, nationality = o.Nationality, ownershipPercent = o.OwnershipPercent, controlType = o.ControlType, pepDeclared = o.PepDeclared }),
            screening = results.Select(r => new { id = r.Id, subjectKind = r.SubjectKind, subjectName = r.SubjectName, listSource = r.ListSource, entryName = r.EntryName, score = r.Score, result = r.Result, resolutionNote = r.ResolutionNote }),
        });
    }

    public async Task<IResult> RescreenAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await compliance.KybCases.SingleOrDefaultAsync(k => k.Id == id, cancellationToken) is not { Status: "InReview" } kyb)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only a case in review can be screened again.");
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await ScreenCaseAsync(kyb, ct);
                await compliance.SaveChangesAsync(ct);
            },
            cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    public async Task<IResult> ResolveScreeningAsync(Guid id, Guid resultId, ScreeningResolutionRequest body, CancellationToken cancellationToken)
    {
        if (await compliance.ScreeningResults.SingleOrDefaultAsync(r => r.Id == resultId && r.KybCaseId == id, cancellationToken) is not { } result)
        {
            return NotFound();
        }

        var note = body.Note?.Trim();
        if (body.Result is not ("Clear" or "ConfirmedMatch") || string.IsNullOrEmpty(note) || note.Length > 1000)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Resolve as Clear or ConfirmedMatch with a note (at most 1000 characters).");
        }

        if (result.Result != "PotentialMatch")
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only a potential match is resolved.");
        }

        result.Result = body.Result;
        result.ResolvedBy = UserId;
        result.ResolutionNote = note;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await compliance.SaveChangesAsync(ct);
                var orgId = await compliance.KybCases.Where(k => k.Id == id).Select(k => k.OrganizationId).SingleAsync(ct);
                await audit.WriteAsync(new AuditEntry("Screening.Resolved", "KybCase", id, orgId, NewState: body.Result, Reason: note,
                    Changes: new Dictionary<string, string?> { ["subject"] = result.SubjectKind, ["list"] = result.ListSource }), ct);
            },
            cancellationToken);
        return Results.Ok(new { id = result.Id, result = result.Result });
    }

    public async Task<IResult> RateRiskAsync(Guid id, RiskRatingRequest body, CancellationToken cancellationToken)
    {
        if (await compliance.KybCases.SingleOrDefaultAsync(k => k.Id == id, cancellationToken) is not { Status: "InReview" } kyb)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only a case in review can be rated.");
        }

        var reason = body.Reason?.Trim();
        if (body.Rating is not { } rating || !Ratings.Contains(rating) || string.IsNullOrEmpty(reason) || reason.Length > 400)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Rate Low, Medium or High with a reason (at most 400 characters).");
        }

        kyb.RiskRating = rating;
        kyb.RiskReason = reason;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await compliance.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("KybCase.RiskRated", "KybCase", id, kyb.OrganizationId, NewState: rating, Reason: reason), ct);
            },
            cancellationToken);
        return Results.Ok(new { riskRating = kyb.RiskRating });
    }

    /// <summary>
    /// The decision (step-up): Approved needs verified representatives, a risk rating and no open or confirmed
    /// screening hit; High risk needs a second, different officer; a confirmed match forces a rejection.
    /// </summary>
    public async Task<IResult> DecideAsync(Guid id, KybDecisionRequest body, CancellationToken cancellationToken)
    {
        if (await compliance.KybCases.SingleOrDefaultAsync(k => k.Id == id, cancellationToken) is not { } kyb)
        {
            return NotFound();
        }

        var reason = body.Reason?.Trim();
        if (body.Decision is not ("Approved" or "Rejected" or "NeedsInfo") || string.IsNullOrEmpty(reason) || reason.Length > 1000)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Decide Approved, Rejected or NeedsInfo with a reason (at most 1000 characters).");
        }

        if (kyb.Status is not ("InReview" or "PendingSecondApproval") || (kyb.Status == "PendingSecondApproval" && body.Decision == "NeedsInfo"))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", $"The case is {kyb.Status}.");
        }

        var results = await compliance.ScreeningResults.AsNoTracking().Where(r => r.KybCaseId == id && r.Result != "Superseded").ToListAsync(cancellationToken);
        if (body.Decision == "Approved")
        {
            if (results.Any(r => r.Result == "ConfirmedMatch"))
            {
                return Problems.Error(StatusCodes.Status409Conflict, "confirmed_match", "A confirmed sanctions match requires a rejection and an escalation note (LQ-33).");
            }

            if (results.Any(r => r.Result == "PotentialMatch"))
            {
                return Problems.Error(StatusCodes.Status409Conflict, "screening_unresolved", "Resolve every potential match first.");
            }

            if (kyb.RiskRating is null || !body.RepresentativesVerified)
            {
                return Problems.Error(StatusCodes.Status409Conflict, "kyb_incomplete", "Rate the risk and verify the representatives against APR first.");
            }

            if (kyb.RiskRating == "High" && kyb.FirstApprover is null)
            {
                kyb.FirstApprover = UserId;
                kyb.Status = "PendingSecondApproval";
                return await SaveDecisionAsync(kyb, "KybCase.FirstApproval", reason, move: null, cancellationToken);
            }

            if (kyb.RiskRating == "High" && kyb.FirstApprover == UserId)
            {
                return Problems.Error(StatusCodes.Status403Forbidden, "four_eyes_required", "A second, different officer must approve a high-risk organization.");
            }

            if (kyb.RiskRating == "High")
            {
                kyb.SecondApprover = UserId;
            }
            else
            {
                kyb.FirstApprover = UserId;
            }

            kyb.Status = "Approved";
            kyb.NextReviewAt = timeProvider.GetUtcNow().AddMonths(kyb.RiskRating switch { "High" => 12, "Medium" => 24, _ => 36 });
            return await SaveDecisionAsync(kyb, "KybCase.Approved", reason, OnboardingStatus.Approved, cancellationToken);
        }

        if (body.Decision == "NeedsInfo" && results.Any(r => r.Result == "ConfirmedMatch"))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "confirmed_match", "A confirmed sanctions match requires a rejection (LQ-33).");
        }

        kyb.Status = body.Decision;
        kyb.FirstApprover ??= UserId;
        return await SaveDecisionAsync(kyb, $"KybCase.{body.Decision}", reason, body.Decision == "Rejected" ? OnboardingStatus.Rejected : OnboardingStatus.Draft, cancellationToken);
    }

    // ----- IKybQuery -----

    public async Task<bool> HasSharingConsentAsync(Guid organizationId, CancellationToken cancellationToken) =>
        (await LatestCaseAsync(organizationId, cancellationToken))?.SharingConsent == true;

    public async Task<IReadOnlyList<BeneficialOwnerSummary>> BeneficialOwnersAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await compliance.BeneficialOwners.AsNoTracking().Where(o => o.OrganizationId == organizationId && o.Current)
            .Select(o => new BeneficialOwnerSummary(o.Name, o.DateOfBirth, o.Nationality, o.OwnershipPercent, o.ControlType, o.PepDeclared))
            .ToListAsync(cancellationToken);

    public async Task<(string? RiskRating, string ScreeningSummary)> SummaryAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (await LatestCaseAsync(organizationId, cancellationToken) is not { } kyb)
        {
            return (null, "Not screened");
        }

        var results = await compliance.ScreeningResults.AsNoTracking().Where(r => r.KybCaseId == kyb.Id && r.Result != "Superseded").Select(r => r.Result).ToListAsync(cancellationToken);
        var summary = results.Contains("ConfirmedMatch") ? "Confirmed match"
            : results.Contains("PotentialMatch") ? "Potential match under review"
            : results.Contains("NoLists") ? "No sanctions lists imported at screening time"
            : "No match";
        return (kyb.RiskRating, summary);
    }

    private Task<KybCase?> LatestCaseAsync(Guid orgId, CancellationToken cancellationToken) =>
        compliance.KybCases.Where(k => k.OrganizationId == orgId).OrderByDescending(k => k.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Screens the organization, its representatives and its current beneficial owners; earlier results are superseded.</summary>
    private async Task ScreenCaseAsync(KybCase kyb, CancellationToken cancellationToken)
    {
        foreach (var earlier in await compliance.ScreeningResults.Where(r => r.KybCaseId == kyb.Id && r.Result != "Superseded").ToListAsync(cancellationToken))
        {
            earlier.Result = "Superseded";
        }

        var org = await organizations.GetAsync(kyb.OrganizationId, cancellationToken);
        var subjects = new List<(string Kind, string Name)>();
        if (org?.LegalName is { } legalName)
        {
            subjects.Add(("Organization", legalName));
        }

        subjects.AddRange((await organizations.AuthorizedSignerNamesAsync(kyb.OrganizationId, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime), cancellationToken)).Select(n => ("Representative", n)));
        subjects.AddRange(await compliance.BeneficialOwners.AsNoTracking().Where(o => o.OrganizationId == kyb.OrganizationId && o.Current).Select(o => new ValueTuple<string, string>("BeneficialOwner", o.Name)).ToListAsync(cancellationToken));
        var now = timeProvider.GetUtcNow();
        foreach (var (kind, name) in subjects)
        {
            var answer = await screening.ScreenAsync(name, cancellationToken);
            if (answer.Hits.Count == 0)
            {
                compliance.ScreeningResults.Add(new ScreeningResult
                {
                    Id = SequentialGuid.NewGuid(),
                    KybCaseId = kyb.Id,
                    SubjectKind = kind,
                    SubjectName = name,
                    Result = answer.Outcome == ScreeningOutcome.NoLists ? "NoLists" : "Clear",
                    ScreenedAt = now,
                });
                continue;
            }

            compliance.ScreeningResults.AddRange(answer.Hits.Select(hit => new ScreeningResult
            {
                Id = SequentialGuid.NewGuid(),
                KybCaseId = kyb.Id,
                SubjectKind = kind,
                SubjectName = name,
                ListSource = hit.ListSource,
                EntryName = hit.EntryName,
                Score = hit.Score,
                Result = "PotentialMatch",
                ScreenedAt = now,
            }));
        }

        await audit.WriteAsync(new AuditEntry("Screening.Run", "KybCase", kyb.Id, kyb.OrganizationId,
            Changes: new Dictionary<string, string?> { ["subjects"] = subjects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }), cancellationToken);
    }

    private async Task<IResult> SaveDecisionAsync(KybCase kyb, string action, string reason, OnboardingStatus? move, CancellationToken cancellationToken)
    {
        kyb.DecisionReason = reason;
        kyb.DecidedAt = timeProvider.GetUtcNow();
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await compliance.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry(action, "KybCase", kyb.Id, kyb.OrganizationId, NewState: kyb.Status, Reason: reason,
                        Changes: new Dictionary<string, string?> { ["riskRating"] = kyb.RiskRating, ["firstApprover"] = kyb.FirstApprover?.ToString(), ["secondApprover"] = kyb.SecondApprover?.ToString() }), ct);
                    if (move is { } to)
                    {
                        await onboarding.MoveAsync(kyb.OrganizationId, to, $"kyb_{kyb.Status}", ct);
                    }
                },
                cancellationToken);
        }
        catch (DomainConflictException conflict)
        {
            return Problems.Error(conflict.Status, conflict.ErrorCode, conflict.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Another officer changed the case first.");
        }

        return Results.Ok(new { id = kyb.Id, status = kyb.Status, nextReviewAt = kyb.NextReviewAt });
    }

    private static IResult NotFound() => Problems.Error(StatusCodes.Status404NotFound, "not_found", "The KYB case does not exist.");
}
