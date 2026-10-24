using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Compliance.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Compliance;

public sealed record PlaceHoldRequest(HoldSubject? SubjectType, Guid SubjectId, HoldKind? Kind, string? Reason);

/// <summary>
/// Holds (task 007, ADR-014): compliance staff place and lift them; lifting a FraudInvestigation hold takes a second,
/// different officer. Every placement and lift is audited.
/// </summary>
internal sealed class HoldService(ComplianceDbContext compliance, UnitOfWork unitOfWork, IAuditWriter audit, IRequestContext request, TimeProvider timeProvider) : IHoldQuery
{
    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    public Task<bool> HasActiveHoldAsync(HoldSubject subject, Guid subjectId, CancellationToken cancellationToken) =>
        compliance.Holds.AsNoTracking().AnyAsync(h => h.SubjectType == subject && h.SubjectId == subjectId && h.LiftedAt == null, cancellationToken);

    public async Task<IResult> ListAsync(CancellationToken cancellationToken) =>
        Results.Ok((await compliance.Holds.AsNoTracking().Where(h => h.LiftedAt == null).OrderByDescending(h => h.PlacedAt).ToListAsync(cancellationToken)).Select(View));

    public async Task<IResult> PlaceAsync(PlaceHoldRequest body, CancellationToken cancellationToken)
    {
        var reason = body.Reason?.Trim();
        if (body.SubjectType is not { } subject || body.Kind is not { } kind || body.SubjectId == Guid.Empty || string.IsNullOrEmpty(reason) || reason.Length > 1000)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give the subject type and id, the hold kind and a reason (at most 1000 characters).");
        }

        var hold = new Hold
        {
            Id = SequentialGuid.NewGuid(),
            SubjectType = subject,
            SubjectId = body.SubjectId,
            Kind = kind,
            Reason = reason,
            PlacedBy = UserId,
            PlacedAt = timeProvider.GetUtcNow(),
        };
        compliance.Holds.Add(hold);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await compliance.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Hold.Placed", "Hold", hold.Id, subject == HoldSubject.Organization ? hold.SubjectId : null,
                    NewState: "Active", Reason: reason, Related: [new AuditReference(subject.ToString(), hold.SubjectId)],
                    Changes: new Dictionary<string, string?> { ["kind"] = kind.ToString() }), ct);
            },
            cancellationToken);
        return Results.Created($"/api/v1/admin/holds/{hold.Id}", View(hold));
    }

    /// <summary>Lifts a hold; a FraudInvestigation hold is first requested by one officer and lifted by another.</summary>
    public async Task<IResult> LiftAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await compliance.Holds.SingleOrDefaultAsync(h => h.Id == id, cancellationToken) is not { } hold)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The hold does not exist.");
        }

        if (hold.LiftedAt is not null)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The hold is already lifted.");
        }

        string action;
        if (hold.Kind == HoldKind.FraudInvestigation && hold.LiftRequestedBy is null)
        {
            hold.LiftRequestedBy = UserId;
            action = "Hold.LiftRequested";
        }
        else if (hold.Kind == HoldKind.FraudInvestigation && hold.LiftRequestedBy == UserId)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "four_eyes_required", "A second officer must lift a FraudInvestigation hold.");
        }
        else
        {
            hold.LiftedBy = UserId;
            hold.LiftedAt = timeProvider.GetUtcNow();
            action = "Hold.Lifted";
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await compliance.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry(action, "Hold", hold.Id, hold.SubjectType == HoldSubject.Organization ? hold.SubjectId : null,
                    PreviousState: "Active", NewState: hold.LiftedAt is null ? "Active" : "Lifted", Related: [new AuditReference(hold.SubjectType.ToString(), hold.SubjectId)]), ct);
            },
            cancellationToken);
        return Results.Ok(View(hold));
    }

    private static object View(Hold hold) => new
    {
        id = hold.Id,
        subjectType = hold.SubjectType,
        subjectId = hold.SubjectId,
        kind = hold.Kind,
        reason = hold.Reason,
        placedAt = hold.PlacedAt,
        liftRequested = hold.LiftRequestedBy is not null,
        liftedAt = hold.LiftedAt,
    };
}
