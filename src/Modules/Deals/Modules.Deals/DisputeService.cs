using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Deals.Persistence;
using Modules.Documents.Contracts;
using Modules.Notifications.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Deals;

public sealed record OpenDisputeRequest(string? Kind, string? Description, IReadOnlyList<Guid>? DocumentIds);

public sealed record DisputeCommentRequest(string? Text, Guid? DocumentId, bool Internal);

public sealed record ResolveDisputeRequest(string? Summary);

/// <summary>
/// Disputes on deals (task 021, product-spec §7.5, ADR-014). The platform records and facilitates; it does not decide
/// who is right (LQ-41). Every dispute except a fee dispute places a <c>Disputed</c> hold on the deal; resolving or
/// withdrawing the last such dispute lifts it. A dispute never changes financial records by itself.
/// </summary>
internal sealed class DisputeService(
    DealsDbContext deals,
    IHoldWriter holds,
    IDocumentQuery documents,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IRequestContext request,
    TimeProvider timeProvider)
{
    public static readonly string[] Kinds = [Dispute.FundingDiscrepancy, "ContractMismatch", "InvoiceDisputedByDebtor", "FraudSuspicion", Dispute.FeeDispute, "Other"];

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    /// <summary><c>POST /deals/{id}/disputes</c> (either party).</summary>
    public async Task<IResult> OpenAsync(Guid dealId, OpenDisputeRequest body, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(dealId, cancellationToken) is not { } deal)
        {
            return NotFound();
        }

        var description = body.Description?.Trim();
        if (body.Kind is not { } kind || !Kinds.Contains(kind) || string.IsNullOrEmpty(description) || description.Length > 1000 || body.DocumentIds?.Count > 10)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", $"Give the kind ({string.Join(", ", Kinds)}), a description (at most 1000 characters) and at most 10 documents.");
        }

        foreach (var documentId in body.DocumentIds ?? [])
        {
            if (await documents.GetAsync(documentId, cancellationToken) is not { ScanStatus: ScanStatus.Clean } document || document.OwnerOrgId != request.OrganizationId)
            {
                return Problems.Error(StatusCodes.Status422UnprocessableEntity, "wrong_document", "Attach clean documents of your organization.");
            }
        }

        var dispute = await OpenCoreAsync(deal, kind, description, UserId, request.OrganizationId, body.DocumentIds ?? [], cancellationToken);
        return Results.Created($"/api/v1/disputes/{dispute.Id}", await ViewAsync(dispute, staff: false, cancellationToken));
    }

    /// <summary>Opens a dispute (also the automatic one of a funding discrepancy, task 017) and places the hold, in its own or the caller's transaction.</summary>
    public async Task<Dispute> OpenCoreAsync(Deal deal, string kind, string reason, Guid? openedBy, Guid? openedByOrg, IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var dispute = new Dispute
        {
            Id = SequentialGuid.NewGuid(),
            DealId = deal.Id,
            Kind = kind,
            Status = Dispute.Open,
            Reason = reason,
            OpenedBy = openedBy,
            OpenedByOrgId = openedByOrg,
            OpenedAt = now,
        };
        deals.Disputes.Add(dispute);
        foreach (var documentId in documentIds)
        {
            deals.DisputeComments.Add(new DisputeComment { Id = SequentialGuid.NewGuid(), DisputeId = dispute.Id, AuthorUserId = openedBy, AuthorOrgId = openedByOrg, DocumentId = documentId, CreatedAt = now });
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await deals.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Dispute.Opened", "Dispute", dispute.Id, deal.SellerOrgId, NewState: Dispute.Open, Reason: kind,
                    Related: [new AuditReference("Deal", deal.Id)], EvidenceDocumentIds: documentIds), ct);
                if (kind != Dispute.FeeDispute)
                {
                    await holds.PlaceSystemHoldAsync(HoldSubject.Deal, deal.Id, HoldKind.Disputed, $"dispute:{dispute.Id}", deal.SellerOrgId, ct);
                }

                // Both parties, and compliance through the audit alert.
                await audit.WriteAsync(new AuditEntry("Compliance.Alert", "Dispute", dispute.Id, deal.SellerOrgId, Reason: "dispute_opened"), ct);
                foreach (var org in new[] { deal.SellerOrgId, deal.FactorOrgId })
                {
                    await outbox.AddAsync(new OrganizationNotificationRequested(org, NotificationTypes.Hold, $"/deals/{deal.Id}", deal.Reference, $"dispute-opened:{dispute.Id}:{org}"), ct);
                }
            },
            cancellationToken);
        return dispute;
    }

    /// <summary><c>GET /disputes/{id}</c>: the parties see it without the staff-internal notes; staff see everything.</summary>
    public async Task<IResult> GetAsync(Guid id, bool staff, CancellationToken cancellationToken)
    {
        if (await VisibleAsync(id, staff, cancellationToken) is not { } dispute)
        {
            return NotFound();
        }

        return Results.Ok(await ViewAsync(dispute, staff, cancellationToken));
    }

    /// <summary><c>GET /deals/{id}/disputes</c>: the deal's disputes for a party.</summary>
    public async Task<IResult> ForDealAsync(Guid dealId, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(dealId, cancellationToken) is null)
        {
            return NotFound();
        }

        var list = await deals.Disputes.AsNoTracking().Where(d => d.DealId == dealId).OrderByDescending(d => d.OpenedAt).ToListAsync(cancellationToken);
        return Results.Ok(list.Select(d => Summary(d)));
    }

    /// <summary><c>POST /disputes/{id}/comments</c>: parties and staff; only staff may write an internal note.</summary>
    public async Task<IResult> CommentAsync(Guid id, DisputeCommentRequest body, bool staff, CancellationToken cancellationToken)
    {
        if (await VisibleAsync(id, staff, cancellationToken) is not { } dispute)
        {
            return NotFound();
        }

        var text = body.Text?.Trim();
        if ((string.IsNullOrEmpty(text) && body.DocumentId is null) || text?.Length > 2000 || (body.Internal && !staff))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Write a comment (at most 2000 characters) or attach a document; internal notes are for staff.");
        }

        if (body.DocumentId is { } documentId
            && (await documents.GetAsync(documentId, cancellationToken) is not { ScanStatus: ScanStatus.Clean } document || (!staff && document.OwnerOrgId != request.OrganizationId)))
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "wrong_document", "Attach a clean document of your organization.");
        }

        var comment = new DisputeComment
        {
            Id = SequentialGuid.NewGuid(),
            DisputeId = dispute.Id,
            AuthorUserId = UserId,
            AuthorOrgId = staff ? null : request.OrganizationId,
            Text = string.IsNullOrEmpty(text) ? null : text,
            DocumentId = body.DocumentId,
            Internal = body.Internal,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        deals.DisputeComments.Add(comment);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await deals.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry(body.Internal ? "Dispute.InternalNoteAdded" : "Dispute.Commented", "Dispute", dispute.Id, null,
                    EvidenceDocumentIds: body.DocumentId is { } doc ? [doc] : null), ct);
            },
            cancellationToken);
        return Results.Created($"/api/v1/disputes/{dispute.Id}", new { id = comment.Id });
    }

    /// <summary><c>POST /disputes/{id}/withdraw</c>: the raising party withdraws an open dispute.</summary>
    public async Task<IResult> WithdrawAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await VisibleAsync(id, staff: false, cancellationToken) is not { } dispute)
        {
            return NotFound();
        }

        if (dispute.OpenedByOrgId != request.OrganizationId)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "forbidden", "Only the party that raised the dispute can withdraw it.");
        }

        return await CloseAsync(dispute, "Withdrawn", summary: null, cancellationToken);
    }

    /// <summary><c>GET /admin/disputes?status=</c>: the staff queue, automatic disputes of task 017 included.</summary>
    public async Task<IResult> QueueAsync(string? status, CancellationToken cancellationToken) =>
        Results.Ok((await deals.Disputes.AsNoTracking().Where(d => d.Status == (status ?? Dispute.Open)).OrderBy(d => d.OpenedAt).Take(200).ToListAsync(cancellationToken))
            .Select(d => Summary(d)));

    /// <summary><c>POST /admin/disputes/{id}/assign</c>: the staff member takes the dispute.</summary>
    public async Task<IResult> AssignAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await deals.Disputes.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is not { Status: Dispute.Open } dispute)
        {
            return NotFound();
        }

        dispute.AssignedTo = UserId;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await deals.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Dispute.Assigned", "Dispute", dispute.Id, null), ct);
            },
            cancellationToken);
        return Results.Ok(Summary(dispute));
    }

    /// <summary>
    /// <c>POST /admin/disputes/{id}/resolve</c> (step-up) with a summary. The hold is lifted when no other open
    /// dispute of the deal needs it; what follows (confirming receipt, a termination, a fee reversal) goes through the
    /// normal commands.
    /// </summary>
    public async Task<IResult> ResolveAsync(Guid id, ResolveDisputeRequest body, CancellationToken cancellationToken)
    {
        var summary = body.Summary?.Trim();
        if (string.IsNullOrEmpty(summary) || summary.Length > 2000)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Write the resolution summary (at most 2000 characters).");
        }

        if (await deals.Disputes.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is not { } dispute)
        {
            return NotFound();
        }

        return await CloseAsync(dispute, "Resolved", summary, cancellationToken);
    }

    private async Task<IResult> CloseAsync(Dispute dispute, string to, string? summary, CancellationToken cancellationToken)
    {
        if (dispute.Status != Dispute.Open)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", $"The dispute is {dispute.Status}.");
        }

        var deal = await deals.Deals.AsNoTracking().SingleAsync(d => d.Id == dispute.DealId, cancellationToken);
        dispute.Status = to;
        dispute.ResolutionSummary = summary;
        dispute.ClosedBy = UserId;
        dispute.ClosedAt = timeProvider.GetUtcNow();
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await deals.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry($"Dispute.{to}", "Dispute", dispute.Id, deal.SellerOrgId, PreviousState: Dispute.Open, NewState: to, Reason: summary,
                        Related: [new AuditReference("Deal", deal.Id)]), ct);
                    if (!await deals.Disputes.AnyAsync(d => d.DealId == deal.Id && d.Status == Dispute.Open && d.Kind != Dispute.FeeDispute, ct))
                    {
                        await holds.LiftSystemHoldAsync(HoldSubject.Deal, deal.Id, HoldKind.Disputed, $"dispute_{to.ToLowerInvariant()}:{dispute.Id}", deal.SellerOrgId, ct);
                    }

                    foreach (var org in new[] { deal.SellerOrgId, deal.FactorOrgId })
                    {
                        await outbox.AddAsync(new OrganizationNotificationRequested(org, NotificationTypes.Hold, $"/deals/{deal.Id}", deal.Reference, $"dispute-{to}:{dispute.Id}:{org}"), ct);
                    }
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The dispute was changed in the meantime.");
        }

        return Results.Ok(Summary(dispute));
    }

    private Task<Deal?> PartyDealAsync(Guid id, CancellationToken cancellationToken) =>
        deals.Deals.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && (d.SellerOrgId == request.OrganizationId || d.FactorOrgId == request.OrganizationId), cancellationToken);

    /// <summary>The dispute if the caller is staff or a party of its deal.</summary>
    private async Task<Dispute?> VisibleAsync(Guid id, bool staff, CancellationToken cancellationToken)
    {
        if (await deals.Disputes.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is not { } dispute)
        {
            return null;
        }

        return staff || await PartyDealAsync(dispute.DealId, cancellationToken) is not null ? dispute : null;
    }

    private async Task<object> ViewAsync(Dispute dispute, bool staff, CancellationToken cancellationToken)
    {
        var comments = await deals.DisputeComments.AsNoTracking()
            .Where(c => c.DisputeId == dispute.Id && (staff || !c.Internal))
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { id = c.Id, authorOrgId = c.AuthorOrgId, byStaff = c.AuthorOrgId == null && c.AuthorUserId != null, text = c.Text, documentId = c.DocumentId, @internal = c.Internal, createdAt = c.CreatedAt })
            .ToListAsync(cancellationToken);
        return new { dispute = Summary(dispute), comments };
    }

    private static object Summary(Dispute d) => new
    {
        id = d.Id,
        dealId = d.DealId,
        kind = d.Kind,
        status = d.Status,
        description = d.Reason,
        openedByOrgId = d.OpenedByOrgId,
        openedAt = d.OpenedAt,
        assignedTo = d.AssignedTo,
        resolutionSummary = d.ResolutionSummary,
        closedAt = d.ClosedAt,
    };

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
}
