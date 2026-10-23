using System.Text.Json;
using Integrations.Sef;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Documents.Contracts;
using Modules.Organizations.Contracts;
using Modules.Receivables.Contracts;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;
using Shared.Infrastructure.Secrets;
using Shared.Kernel;

namespace Modules.Receivables;

public sealed record DeclarationRequest(IReadOnlyDictionary<string, bool>? Answers);

public sealed record AttachDocumentRequest(Guid DocumentId);

/// <param name="Decision"><c>Verified</c> or <c>Rejected</c>.</param>
/// <param name="ReasonCode">Required for a rejection, e.g. <c>Fraud</c> (which blocks re-import of the invoice).</param>
public sealed record ReviewDecisionRequest(string Decision, string? ReasonCode, string? Comment);

/// <summary>
/// Declarations, supporting documents, submission with the check pipeline, cancellation and the compliance review
/// (task 011). No path verifies a receivable without a fresh SEF status of Approved.
/// </summary>
internal sealed class VerificationService(
    ReceivablesDbContext receivables,
    IEnumerable<IReceivableCheck> checks,
    IOrganizationQuery organizations,
    IHoldQuery holds,
    IDocumentQuery documents,
    ISefClient sef,
    ISefConnections connections,
    ISecretStore secrets,
    IBusinessParameters parameters,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IRequestContext request,
    TimeProvider timeProvider)
{
    public const string FraudReason = "Fraud";

    private const int MaxCommentLength = 1000;

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    public async Task<IResult> CurrentDeclarationAsync(CancellationToken cancellationToken)
    {
        var version = await CurrentVersionAsync(cancellationToken);
        return Results.Ok(new { version = version.Version, text = version.Text, items = JsonSerializer.Deserialize<string[]>(version.ItemCodes), sha256 = version.Sha256 });
    }

    /// <summary>Saves the answers to the current declaration version; a "no" is kept but blocks submission.</summary>
    public async Task<IResult> SaveDeclarationAsync(Guid id, DeclarationRequest body, CancellationToken cancellationToken)
    {
        if (await receivables.Receivables.SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is not { } receivable)
        {
            return NotFound();
        }

        if (receivable.State != ReceivableState.Draft)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Declarations can be changed only while the receivable is a Draft.");
        }

        var version = await CurrentVersionAsync(cancellationToken);
        var items = JsonSerializer.Deserialize<string[]>(version.ItemCodes)!;
        if (body.Answers is null || items.Any(item => !body.Answers.ContainsKey(item)) || body.Answers.Keys.Any(key => !items.Contains(key)))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", $"Answer every item of declaration {version.Version}: {string.Join(", ", items)}.");
        }

        var declaration = new SellerDeclaration
        {
            Id = SequentialGuid.NewGuid(),
            ReceivableId = receivable.Id,
            Version = version.Version,
            Answers = JsonSerializer.Serialize(items.ToDictionary(item => item, item => body.Answers[item])),
            AllConfirmed = items.All(item => body.Answers[item]),
            AcceptedBy = UserId,
            AcceptedAt = timeProvider.GetUtcNow(),
            IpAddress = request.IpAddress,
        };
        receivables.SellerDeclarations.Add(declaration);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await receivables.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Receivable.DeclarationSaved", "Receivable", receivable.Id, receivable.SellerOrgId,
                    Changes: new Dictionary<string, string?> { ["version"] = version.Version, ["allConfirmed"] = declaration.AllConfirmed.ToString() }), ct);
            },
            cancellationToken);
        var refused = items.Where(item => !body.Answers[item]).ToList();
        return Results.Ok(new
        {
            version = version.Version,
            complete = declaration.AllConfirmed,
            blocking = refused.Count == 0 ? null : $"These declarations were answered \"no\", so the receivable cannot be submitted: {string.Join(", ", refused)}.",
        });
    }

    /// <summary>Attaches an uploaded document of the seller (purpose ReceivableEvidence: contract, order, delivery proof).</summary>
    public async Task<IResult> AttachDocumentAsync(Guid id, AttachDocumentRequest body, CancellationToken cancellationToken)
    {
        if (await receivables.Receivables.SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is not { } receivable)
        {
            return NotFound();
        }

        if (receivable.State != ReceivableState.Draft)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Documents can be attached only while the receivable is a Draft.");
        }

        var document = await documents.GetAsync(body.DocumentId, cancellationToken);
        if (document is null || document.OwnerOrgId != receivable.SellerOrgId)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The document does not exist.");
        }

        if (document.Purpose != DocumentPurpose.ReceivableEvidence)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "wrong_document_purpose", "Upload the document with purpose ReceivableEvidence.");
        }

        if (await receivables.ReceivableDocuments.AnyAsync(d => d.ReceivableId == id && d.DocumentId == document.Id, cancellationToken))
        {
            return Results.NoContent();
        }

        receivables.ReceivableDocuments.Add(new ReceivableDocument { ReceivableId = id, DocumentId = document.Id, AttachedBy = UserId, AttachedAt = timeProvider.GetUtcNow() });
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await receivables.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Receivable.DocumentAttached", "Receivable", id, receivable.SellerOrgId, EvidenceDocumentIds: [document.Id]), ct);
            },
            cancellationToken);
        return Results.NoContent();
    }

    public async Task<IResult> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await receivables.Receivables.SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is not { } receivable)
        {
            return NotFound();
        }

        if (receivable.State != ReceivableState.Draft)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only a Draft receivable can be submitted.");
        }

        if (await holds.HasActiveHoldAsync(HoldSubject.Receivable, id, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status423Locked, "hold_active", "The receivable is on hold.");
        }

        if (!await DeclarationCompleteAsync(id, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "declaration_incomplete", "Confirm every item of the current declaration first.");
        }

        if (!await HasCleanSupportingDocumentAsync(id, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "documents_missing", "Attach at least one clean supporting document (contract, order or delivery proof).");
        }

        receivable.State = ReceivableState.PendingVerification;
        if (!await CommitAsync(
            receivable.SellerOrgId,
            ct => audit.WriteAsync(new AuditEntry("Receivable.Submitted", "Receivable", id, receivable.SellerOrgId,
                PreviousState: nameof(ReceivableState.Draft), NewState: nameof(ReceivableState.PendingVerification)), ct),
            cancellationToken))
        {
            return ChangedConcurrently();
        }

        await VerifyAsync(receivable, cancellationToken);
        return Results.Accepted(value: new { state = receivable.State, reviewRequired = receivable.ReviewRequired, reason = receivable.RejectionReason });
    }

    public async Task<IResult> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await receivables.Receivables.SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is not { } receivable)
        {
            return NotFound();
        }

        if (receivable.State is not (ReceivableState.Draft or ReceivableState.Verified))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only a Draft or Verified receivable can be cancelled.");
        }

        var previous = receivable.State;
        receivable.State = ReceivableState.Cancelled;
        return await CommitAsync(
            receivable.SellerOrgId,
            ct => audit.WriteAsync(new AuditEntry("Receivable.Cancelled", "Receivable", id, receivable.SellerOrgId,
                PreviousState: previous.ToString(), NewState: nameof(ReceivableState.Cancelled)), ct),
            cancellationToken)
            ? Results.Ok(new { state = receivable.State })
            : ChangedConcurrently();
    }

    /// <summary>The seller's view of the checks; the cross-seller duplicate check shows a neutral text, never its result.</summary>
    public async Task<IResult> ChecksAsync(Guid id, CancellationToken cancellationToken) =>
        await receivables.Receivables.AnyAsync(r => r.Id == id, cancellationToken)
            ? Results.Ok(await CheckRowsAsync(id, forSeller: true, cancellationToken))
            : NotFound();

    /// <summary>The manual review queue across all sellers (compliance staff only).</summary>
    public async Task<IResult> ReviewQueueAsync(CancellationToken cancellationToken) =>
        Results.Ok(await receivables.Receivables.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.State == ReceivableState.PendingVerification && r.ReviewRequired)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { id = r.Id, sellerOrgId = r.SellerOrgId, debtorId = r.DebtorId, maturityDate = r.MaturityDate, createdAt = r.CreatedAt })
            .ToListAsync(cancellationToken));

    public async Task<IResult> ReviewDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await receivables.Receivables.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is not { } receivable)
        {
            return NotFound();
        }

        var invoice = await receivables.Invoices.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == receivable.InvoiceId, cancellationToken);
        var debtor = await receivables.Debtors.AsNoTracking().SingleAsync(d => d.Id == receivable.DebtorId, cancellationToken);
        var declaration = await receivables.SellerDeclarations.AsNoTracking().Where(d => d.ReceivableId == id).OrderByDescending(d => d.AcceptedAt).FirstOrDefaultAsync(cancellationToken);
        var sefHistory = await receivables.SefStatusHistory.AsNoTracking().Where(h => h.InvoiceId == invoice.Id).OrderByDescending(h => h.ObservedAt)
            .Select(h => new { status = h.Status, observedAt = h.ObservedAt }).ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            id = receivable.Id,
            state = receivable.State,
            reviewRequired = receivable.ReviewRequired,
            sellerOrgId = receivable.SellerOrgId,
            faceValue = receivable.FaceValue.Round(),
            maturityDate = receivable.MaturityDate,
            invoice = new { sefInvoiceId = invoice.SefInvoiceId, number = invoice.InvoiceNumber, issueDate = invoice.IssueDate, dueDate = invoice.DueDate, ublDocumentId = invoice.UblDocumentId, ublSha256 = invoice.UblSha256 },
            debtor = new { pib = debtor.Pib, legalName = debtor.LegalName },
            declaration = declaration is null ? null : new { version = declaration.Version, allConfirmed = declaration.AllConfirmed, acceptedAt = declaration.AcceptedAt },
            documents = await receivables.ReceivableDocuments.AsNoTracking().Where(d => d.ReceivableId == id).Select(d => d.DocumentId).ToListAsync(cancellationToken),
            checks = await CheckRowsAsync(id, forSeller: false, cancellationToken),
            sefHistory,
        });
    }

    /// <summary>
    /// The compliance decision on a receivable in the manual queue (step-up). Verifying re-polls SEF and re-runs every
    /// check, so a stale buyer approval, a seller that lost its approval or a deadline that passed while the receivable
    /// waited can never be verified by hand.
    /// </summary>
    public async Task<IResult> DecideAsync(Guid id, ReviewDecisionRequest body, CancellationToken cancellationToken)
    {
        if (await receivables.Receivables.IgnoreQueryFilters().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is not { } receivable)
        {
            return NotFound();
        }

        if (receivable.State != ReceivableState.PendingVerification || !receivable.ReviewRequired)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The receivable is not waiting for a manual decision.");
        }

        var verify = body.Decision == nameof(ReceivableState.Verified);
        var reasonCode = NormalizeReasonCode(body.ReasonCode);
        if ((!verify && (body.Decision != nameof(ReceivableState.Rejected) || reasonCode is null)) || body.Comment is { Length: > MaxCommentLength })
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed",
                $"Decide Verified, or Rejected with a reason code of letters only (e.g. {FraudReason}); a comment has at most {MaxCommentLength} characters.");
        }

        if (verify)
        {
            if (await holds.HasActiveHoldAsync(HoldSubject.Receivable, id, cancellationToken))
            {
                return Problems.Error(StatusCodes.Status423Locked, "hold_active", "The receivable is on hold.");
            }

            var invoice = await receivables.Invoices.IgnoreQueryFilters().SingleAsync(i => i.Id == receivable.InvoiceId, cancellationToken);
            var sefStatus = await FreshSefStatusAsync(receivable.SellerOrgId, invoice, cancellationToken);
            if (sefStatus != "Approved")
            {
                // The observation (if SEF answered) is kept, with its audit event; the receivable stays in the queue.
                await CommitAsync(receivable.SellerOrgId, _ => Task.CompletedTask, cancellationToken);
                return Problems.Error(StatusCodes.Status409Conflict, "sef_not_approved", "SEF does not currently show the invoice as approved by the buyer.");
            }

            var rows = await RunChecksAsync(receivable, invoice, sefStatus, cancellationToken);
            var failed = rows.Where(r => r.Result == CheckResult.Fail).Select(r => r.Code).ToList();
            if (failed.Count > 0)
            {
                await CommitAsync(receivable.SellerOrgId, ct => AuditChecksAsync(receivable, rows, ct), cancellationToken);
                return Problems.Error(StatusCodes.Status409Conflict, "checks_failed", $"These checks fail now: {string.Join(", ", failed)}.");
            }

            receivable.State = ReceivableState.Verified;
            receivable.ReviewRequired = false;
            return await CommitAsync(
                receivable.SellerOrgId,
                async ct =>
                {
                    await AuditChecksAsync(receivable, rows, ct);
                    await audit.WriteAsync(new AuditEntry("Receivable.Verified", "Receivable", id, receivable.SellerOrgId,
                        PreviousState: nameof(ReceivableState.PendingVerification), NewState: nameof(ReceivableState.Verified), Reason: body.Comment), ct);
                },
                cancellationToken)
                ? Results.Ok(new { state = receivable.State })
                : ChangedConcurrently();
        }

        receivable.State = ReceivableState.Rejected;
        receivable.ReviewRequired = false;
        receivable.RejectionReason = reasonCode;
        return await CommitAsync(
            receivable.SellerOrgId,
            ct => audit.WriteAsync(new AuditEntry("Receivable.Rejected", "Receivable", id, receivable.SellerOrgId,
                PreviousState: nameof(ReceivableState.PendingVerification), NewState: nameof(ReceivableState.Rejected),
                Reason: $"{reasonCode}: {body.Comment}"), ct),
            cancellationToken)
            ? Results.Ok(new { state = receivable.State })
            : ChangedConcurrently();
    }

    /// <summary>A reason code is 1–40 letters; any spelling of Fraud becomes <see cref="FraudReason"/>, so the re-import block always holds.</summary>
    private static string? NormalizeReasonCode(string? reasonCode)
    {
        var code = reasonCode?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length > 40 || !code.All(char.IsAsciiLetter))
        {
            return null;
        }

        return string.Equals(code, FraudReason, StringComparison.OrdinalIgnoreCase) ? FraudReason : code;
    }

    /// <summary>Runs every check and decides: any Fail → Rejected; any Warn or a large amount → manual queue; else Verified.</summary>
    private async Task VerifyAsync(Receivable receivable, CancellationToken cancellationToken)
    {
        var invoice = await receivables.Invoices.SingleAsync(i => i.Id == receivable.InvoiceId, cancellationToken);
        var sefStatus = await FreshSefStatusAsync(receivable.SellerOrgId, invoice, cancellationToken);
        if (sefStatus is null)
        {
            // SEF unavailable or the key stopped working: the receivable stays PendingVerification (fail closed); the
            // re-check job of task 020 runs the verification later.
            return;
        }

        var rows = await RunChecksAsync(receivable, invoice, sefStatus, cancellationToken);
        var failed = rows.Where(r => r.Result == CheckResult.Fail).Select(r => r.Code).ToList();
        var autoApproveLimit = new Money(
            await parameters.GetAsync(ReceivableParameters.AutoApproveMaxFaceValue, ReceivableParameters.DefaultAutoApproveMaxFaceValue, cancellationToken),
            ReceivableParameters.LimitCurrency);
        var manual = failed.Count == 0
            && (rows.Any(r => r.Result == CheckResult.Warn) || receivable.FaceValue.Currency != autoApproveLimit.Currency || receivable.FaceValue > autoApproveLimit);
        receivable.State = failed.Count > 0 ? ReceivableState.Rejected : manual ? ReceivableState.PendingVerification : ReceivableState.Verified;
        receivable.ReviewRequired = manual;
        receivable.RejectionReason = failed.Count > 0 ? $"checks_failed: {string.Join(",", failed)}" : null;

        await CommitAsync(
            receivable.SellerOrgId,
            async ct =>
            {
                await AuditChecksAsync(receivable, rows, ct);
                await audit.WriteAsync(new AuditEntry(
                    failed.Count > 0 ? "Receivable.Rejected" : manual ? "Receivable.SentToManualReview" : "Receivable.Verified",
                    "Receivable", receivable.Id, receivable.SellerOrgId,
                    PreviousState: nameof(ReceivableState.PendingVerification), NewState: receivable.State.ToString(), Reason: receivable.RejectionReason), ct);
            },
            cancellationToken);
    }

    /// <summary>A new run of every check, added to the context (append-only rows; saved by the caller's commit).</summary>
    private async Task<List<VerificationCheck>> RunChecksAsync(Receivable receivable, Invoice invoice, string sefStatus, CancellationToken cancellationToken)
    {
        var context = new CheckContext(
            receivable,
            invoice,
            await receivables.Debtors.SingleAsync(d => d.Id == receivable.DebtorId, cancellationToken),
            (await organizations.GetAsync(receivable.SellerOrgId, cancellationToken))!,
            await holds.HasActiveHoldAsync(HoldSubject.Organization, receivable.SellerOrgId, cancellationToken),
            sefStatus,
            await DeclarationCompleteAsync(receivable.Id, cancellationToken),
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));

        var run = (await receivables.VerificationChecks.Where(c => c.ReceivableId == receivable.Id).MaxAsync(c => (int?)c.Run, cancellationToken) ?? 0) + 1;
        var now = timeProvider.GetUtcNow();
        var rows = new List<VerificationCheck>();
        foreach (var check in checks)
        {
            var outcome = await check.RunAsync(context, cancellationToken);
            rows.Add(new VerificationCheck
            {
                Id = SequentialGuid.NewGuid(),
                ReceivableId = receivable.Id,
                Run = run,
                Code = check.Code,
                Result = outcome.Result,
                Details = outcome.Details?[..Math.Min(outcome.Details.Length, 400)],
                RanAt = now,
            });
        }

        receivables.VerificationChecks.AddRange(rows);
        return rows;
    }

    private async Task AuditChecksAsync(Receivable receivable, List<VerificationCheck> rows, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            await audit.WriteAsync(new AuditEntry("Receivable.CheckRecorded", "Receivable", receivable.Id, receivable.SellerOrgId,
                Changes: new Dictionary<string, string?>
                {
                    ["code"] = row.Code,
                    ["result"] = row.Result.ToString(),
                    ["run"] = row.Run.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }), cancellationToken);
        }
    }

    /// <summary>
    /// Saves every tracked change (state, checks, SEF observations) with the audit events in one transaction; the SEF
    /// observations get their own events here. False when another request changed the receivable first.
    /// </summary>
    private async Task<bool> CommitAsync(Guid sellerOrgId, Func<CancellationToken, Task> audited, CancellationToken cancellationToken)
    {
        var observations = receivables.ChangeTracker.Entries<SefStatusObservation>()
            .Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await receivables.SaveChangesAsync(ct);
                    foreach (var observation in observations)
                    {
                        await audit.WriteAsync(new AuditEntry("Invoice.SefStatusObserved", "Invoice", observation.InvoiceId, sellerOrgId,
                            NewState: observation.Status), ct);
                    }

                    await audited(ct);
                },
                cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            receivables.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>The invoice's status re-polled from SEF and added to the history (saved by the next commit); null when SEF cannot be asked.</summary>
    private async Task<string?> FreshSefStatusAsync(Guid sellerOrgId, Invoice invoice, CancellationToken cancellationToken)
    {
        var connection = await connections.GetAsync(sellerOrgId, cancellationToken);
        var apiKey = connection is { Status: SefConnectionStatus.Connected } ? await secrets.GetAsync(connection.SecretName, cancellationToken) : null;
        if (apiKey is null)
        {
            return null;
        }

        SefSalesInvoice? fresh;
        try
        {
            fresh = await sef.GetSalesInvoiceAsync(apiKey, long.Parse(invoice.SefInvoiceId, System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        }
        catch (SefUnauthorizedException)
        {
            await connections.MarkInvalidAsync(sellerOrgId, cancellationToken);
            return null;
        }
        catch (SefUnavailableException)
        {
            return null;
        }

        var status = fresh?.Status ?? "Deleted";
        var now = timeProvider.GetUtcNow();
        invoice.SefStatus = status;
        invoice.SefStatusCheckedAt = now;
        receivables.SefStatusHistory.Add(new SefStatusObservation
        {
            Id = SequentialGuid.NewGuid(),
            InvoiceId = invoice.Id,
            Status = status,
            ObservedAt = now,
            Raw = JsonSerializer.Serialize(fresh is null ? new { status } : (object)fresh),
        });
        return status;
    }

    private async Task<bool> DeclarationCompleteAsync(Guid receivableId, CancellationToken cancellationToken)
    {
        var current = await CurrentVersionAsync(cancellationToken);
        var latest = await receivables.SellerDeclarations.AsNoTracking()
            .Where(d => d.ReceivableId == receivableId)
            .OrderByDescending(d => d.AcceptedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return latest is { AllConfirmed: true } && latest.Version == current.Version;
    }

    private async Task<bool> HasCleanSupportingDocumentAsync(Guid receivableId, CancellationToken cancellationToken)
    {
        var attached = await receivables.ReceivableDocuments.AsNoTracking().Where(d => d.ReceivableId == receivableId).Select(d => d.DocumentId).ToListAsync(cancellationToken);
        foreach (var documentId in attached)
        {
            if (await documents.GetAsync(documentId, cancellationToken) is { ScanStatus: ScanStatus.Clean })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The newest declaration version (DRAFT-0 until task 000 delivers the approved text).</summary>
    private Task<DeclarationVersion> CurrentVersionAsync(CancellationToken cancellationToken) =>
        receivables.DeclarationVersions.AsNoTracking().OrderByDescending(v => v.CreatedAt).FirstAsync(cancellationToken);

    private async Task<List<object>> CheckRowsAsync(Guid id, bool forSeller, CancellationToken cancellationToken) =>
        [.. (await receivables.VerificationChecks.AsNoTracking().Where(c => c.ReceivableId == id).OrderBy(c => c.Run).ThenBy(c => c.RanAt).ToListAsync(cancellationToken))
            .Select(c => forSeller && c.Code == DuplicateFingerprintCheck.CheckCode
                ? (object)new { run = c.Run, code = c.Code, result = "Review", details = DuplicateFingerprintCheck.SellerDetails, ranAt = c.RanAt }
                : new { run = c.Run, code = c.Code, result = c.Result.ToString(), details = c.Details, ranAt = c.RanAt })];

    private static IResult NotFound() => Problems.Error(StatusCodes.Status404NotFound, "not_found", "The receivable does not exist.");

    private static IResult ChangedConcurrently() =>
        Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Another request changed the receivable first. Reload it and try again.");
}
