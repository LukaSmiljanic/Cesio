using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Deals.Contracts;
using Modules.Deals.Persistence;
using Modules.Documents.Contracts;
using Modules.Organizations.Contracts;
using Modules.Receivables.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Deals;

public sealed record CddDecisionRequest(string? Decision, string? Reason);

public sealed record ContractRequest(Guid DocumentId);

public sealed record TermsConfirmationRequest(Guid ContractDocumentId, bool Confirmed);

public sealed record CancelDealRequest(string? ReasonCode, string? Comment);

/// <summary>
/// The deal steps before signing (task 015, product-spec.md §6.5 steps 1–2): the factor's CDD gate, its agreement,
/// both parties' terms confirmation on the same document hash, and cancellation. Every step is audited with its actor
/// and blocked while a hold is active on the deal, the receivable or either organization.
/// </summary>
internal sealed class DealStepService(
    DealsDbContext deals,
    IReceivableWriter receivables,
    IReceivableMarket receivableMarket,
    ISignatureValidator signatureValidator,
    IOrganizationQuery organizations,
    IHoldQuery holds,
    IDocumentQuery documents,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IRequestContext request,
    TimeProvider timeProvider)
{
    /// <summary>Before signing, either party may still end the deal (product-spec.md §7.4).</summary>
    private static readonly DealState[] BeforeSigning = [DealState.AwaitingFactorCdd, DealState.AwaitingContract, DealState.AwaitingSignatures];

    private Guid OrganizationId => request.OrganizationId ?? throw new InvalidOperationException("An organization context is required.");

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    public async Task<IResult> CddDecisionAsync(Guid id, CddDecisionRequest body, DateTimeOffset stepUpAt, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal || deal.FactorOrgId != OrganizationId)
        {
            return NotFound();
        }

        var approved = body.Decision == "Approved";
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();
        if ((!approved && (body.Decision != "Declined" || reason is null)) || reason is { Length: > 400 })
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Decide Approved, or Declined with a reason of at most 400 characters.");
        }

        if (await RefusalAsync(deal, DealState.AwaitingFactorCdd, signatory: true, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        deals.CddDecisions.Add(new CddDecision
        {
            Id = SequentialGuid.NewGuid(),
            DealId = id,
            Approved = approved,
            Reason = reason,
            DecidedBy = UserId,
            DecidedAt = timeProvider.GetUtcNow(),
            StepUpAt = stepUpAt,
        });
        return await CommitAsync(
            async ct =>
            {
                await audit.WriteAsync(new AuditEntry(approved ? "Deal.CddApproved" : "Deal.CddDeclined", "Deal", id, OrganizationId, Reason: reason,
                    Changes: new Dictionary<string, string?> { ["stepUpAt"] = stepUpAt.ToString("O") }), ct);
                if (approved)
                {
                    await TransitionAsync(deal, DealState.AwaitingContract, "cdd_approved", UserId, ct);
                }
                else
                {
                    await CancelCoreAsync(deal, "cdd_declined", UserId, ct);
                }
            },
            deal,
            cancellationToken);
    }

    /// <summary>The factor uploads (or replaces) its agreement; a replacement makes earlier confirmations void.</summary>
    public async Task<IResult> UploadContractAsync(Guid id, ContractRequest body, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal || deal.FactorOrgId != OrganizationId)
        {
            return NotFound();
        }

        if (await RefusalAsync(deal, DealState.AwaitingContract, signatory: false, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var document = await documents.GetAsync(body.DocumentId, cancellationToken);
        if (document is null || document.OwnerOrgId != OrganizationId)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The document does not exist.");
        }

        if (document.Purpose != DocumentPurpose.Contract || document.ContentType != "application/pdf" || document.ScanStatus != ScanStatus.Clean)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "wrong_document", "Upload the agreement as a clean PDF with purpose Contract.");
        }

        var replaces = await deals.ContractDocuments.AnyAsync(c => c.DealId == id, cancellationToken);
        var contract = new ContractDocument
        {
            Id = SequentialGuid.NewGuid(),
            DealId = id,
            DocumentId = document.Id,
            Sha256 = document.Sha256,
            UploadedBy = UserId,
            UploadedAt = timeProvider.GetUtcNow(),
        };
        deals.ContractDocuments.Add(contract);
        return await CommitAsync(
            ct => audit.WriteAsync(new AuditEntry(replaces ? "Deal.ContractReplaced" : "Deal.ContractUploaded", "Deal", id, OrganizationId,
                EvidenceDocumentIds: [document.Id], Changes: new Dictionary<string, string?> { ["sha256"] = document.Sha256 }), ct),
            deal,
            cancellationToken,
            () => Results.Ok(new { contractDocumentId = contract.Id, documentId = document.Id, sha256 = document.Sha256 }));
    }

    /// <summary>
    /// A party's Signatory confirms that the current agreement matches the deal terms; when both parties confirmed
    /// the same version, its hash is fixed for signing and the deal awaits signatures.
    /// </summary>
    public async Task<IResult> ConfirmTermsAsync(Guid id, TermsConfirmationRequest body, DateTimeOffset stepUpAt, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal)
        {
            return NotFound();
        }

        if (!body.Confirmed)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Confirm that the agreement matches the deal terms, or ask the factor for a corrected one.");
        }

        if (await RefusalAsync(deal, DealState.AwaitingContract, signatory: true, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var current = await deals.ContractDocuments.Where(c => c.DealId == id).OrderByDescending(c => c.UploadedAt).FirstOrDefaultAsync(cancellationToken);
        if (current is null || current.Id != body.ContractDocumentId)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "contract_changed", "Confirm the current agreement; it was replaced or not uploaded yet.");
        }

        if (await deals.TermsConfirmations.AnyAsync(c => c.ContractDocumentId == current.Id && c.PartyOrgId == OrganizationId, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "already_confirmed", "Your organization already confirmed this agreement.");
        }

        var now = timeProvider.GetUtcNow();
        deals.TermsConfirmations.Add(new TermsConfirmation
        {
            Id = SequentialGuid.NewGuid(),
            DealId = id,
            ContractDocumentId = current.Id,
            Sha256 = current.Sha256,
            PartyOrgId = OrganizationId,
            ConfirmedBy = UserId,
            ConfirmedAt = now,
            StepUpAt = stepUpAt,
        });
        var otherParty = OrganizationId == deal.SellerOrgId ? deal.FactorOrgId : deal.SellerOrgId;
        var bothConfirmed = await deals.TermsConfirmations.AnyAsync(c => c.ContractDocumentId == current.Id && c.PartyOrgId == otherParty, cancellationToken);
        if (bothConfirmed)
        {
            deal.AgreedContractSha256 = current.Sha256;
        }

        return await CommitAsync(
            async ct =>
            {
                await audit.WriteAsync(new AuditEntry("Deal.TermsConfirmed", "Deal", id, OrganizationId, EvidenceDocumentIds: [current.DocumentId],
                    Changes: new Dictionary<string, string?> { ["sha256"] = current.Sha256, ["stepUpAt"] = stepUpAt.ToString("O") }), ct);
                if (bothConfirmed)
                {
                    await audit.WriteAsync(new AuditEntry("Deal.ContractAgreed", "Deal", id, deal.SellerOrgId, EvidenceDocumentIds: [current.DocumentId],
                        Changes: new Dictionary<string, string?> { ["agreedContractSha256"] = current.Sha256 }), ct);
                    await TransitionAsync(deal, DealState.AwaitingSignatures, "terms_confirmed_by_both", UserId, ct);
                }
            },
            deal,
            cancellationToken);
    }

    /// <summary>Either party's Signatory ends the deal before signing; the receivable returns to Verified, the listing stays closed.</summary>
    public async Task<IResult> CancelAsync(Guid id, CancelDealRequest body, DateTimeOffset stepUpAt, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal)
        {
            return NotFound();
        }

        var code = body.ReasonCode?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length > 40 || !code.All(char.IsAsciiLetter) || body.Comment is { Length: > 1000 })
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give a reason code of letters only; a comment has at most 1000 characters.");
        }

        if (!BeforeSigning.Contains(deal.State))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only a deal that is not signed yet can be cancelled.");
        }

        if (await RefusalAsync(deal, deal.State, signatory: true, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        return await CommitAsync(
            async ct =>
            {
                await audit.WriteAsync(new AuditEntry("Deal.CancelRequested", "Deal", id, OrganizationId, Reason: $"{code}: {body.Comment}",
                    Changes: new Dictionary<string, string?> { ["stepUpAt"] = stepUpAt.ToString("O") }), ct);
                await CancelCoreAsync(deal, code, UserId, ct);
            },
            deal,
            cancellationToken);
    }

    /// <summary>
    /// A party uploads the signed agreement (task 016, api.md §3.7). Every signature is validated and stored with its
    /// report; when each party has at least one valid signature of a legal representative over the agreed contract,
    /// SEF is fresh and approved and no hold is active, the deal becomes Signed and the receivable Assigned in one
    /// transaction.
    /// </summary>
    public async Task<IResult> UploadSignedContractAsync(Guid id, ContractRequest body, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal)
        {
            return NotFound();
        }

        if (deal.State == DealState.AwaitingSignatures && deal.SignatureDeadline <= timeProvider.GetUtcNow())
        {
            return Problems.Error(StatusCodes.Status409Conflict, "deadline_passed", "The signature deadline has passed.");
        }

        if (await RefusalAsync(deal, DealState.AwaitingSignatures, signatory: true, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var document = await documents.GetAsync(body.DocumentId, cancellationToken);
        if (document is null || document.OwnerOrgId != OrganizationId)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The document does not exist.");
        }

        if (document.Purpose != DocumentPurpose.Contract || document.ContentType != "application/pdf"
            || await documents.ReadCleanAsync(document.Id, cancellationToken) is not { } pdf)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "wrong_document", "Upload the signed agreement as a clean PDF with purpose Contract.");
        }

        var validation = await signatureValidator.ValidateAsync(pdf, cancellationToken);
        if (validation.Outcome == ValidationOutcome.Unavailable)
        {
            return Problems.Error(StatusCodes.Status503ServiceUnavailable, "upstream_unavailable", "The signatures cannot be validated right now; try again later.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var sellerSigners = Names(await organizations.AuthorizedSignerNamesAsync(deal.SellerOrgId, today, cancellationToken));
        var factorSigners = Names(await organizations.AuthorizedSignerNamesAsync(deal.FactorOrgId, today, cancellationToken));
        var now = timeProvider.GetUtcNow();
        var rows = validation.Signatures.Select(check =>
        {
            var name = Normalize(check.SignerName);
            var party = sellerSigners.Contains(name) ? "Seller" : factorSigners.Contains(name) ? "Factor" : null;
            return new Signature
            {
                Id = SequentialGuid.NewGuid(),
                DealId = id,
                DocumentId = document.Id,
                Party = party,
                SignerName = check.SignerName,
                CertificateSubject = check.CertificateSubject,
                CertificateIssuer = check.CertificateIssuer,
                CertificateSerial = check.CertificateSerial,
                SigningTime = check.SigningTime,
                Result = Judge(check, party, deal.AgreedContractSha256),
                Report = check.ReportJson,
                UploadedBy = UserId,
                ValidatedAt = now,
            };
        }).ToList();
        if (rows.Count == 0)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "signature_invalid", "The document carries no signature.");
        }

        deals.Signatures.AddRange(rows);
        var earlier = await deals.Signatures.AsNoTracking().Where(s => s.DealId == id && s.Result == "Valid").Select(s => s.Party).ToListAsync(cancellationToken);
        var validParties = earlier.Concat(rows.Where(r => r.Result == "Valid").Select(r => r.Party)).ToHashSet();
        var signed = validParties.Contains("Seller") && validParties.Contains("Factor");
        if (signed)
        {
            switch (await receivableMarket.EnsureFreshApprovalAsync(deal.ReceivableId, TimeSpan.FromHours(1), cancellationToken))
            {
                case SefApproval.Unavailable:
                    return Problems.Error(StatusCodes.Status503ServiceUnavailable, "upstream_unavailable", "SEF cannot be reached to confirm the buyer's approval; try again later.");
                case SefApproval.NotApproved:
                    signed = false;
                    break;
            }
        }

        var sefChanged = validParties.Contains("Seller") && validParties.Contains("Factor") && !signed;
        var outcome = await CommitAsync(
            async ct =>
            {
                foreach (var row in rows)
                {
                    await audit.WriteAsync(new AuditEntry("Deal.SignatureValidated", "Deal", id, OrganizationId, EvidenceDocumentIds: [document.Id],
                        Changes: new Dictionary<string, string?> { ["signer"] = row.SignerName, ["party"] = row.Party, ["result"] = row.Result, ["serial"] = row.CertificateSerial }), ct);
                }

                if (signed)
                {
                    await TransitionAsync(deal, DealState.Signed, "signatures_valid", UserId, ct);
                    await receivables.AssignAsync(deal.ReceivableId, id, ct);
                    await outbox.AddAsync(new DealSigned(id, deal.ReceivableId, deal.SellerOrgId, deal.FactorOrgId), ct);
                }
            },
            deal,
            cancellationToken,
            () => Results.Ok(new { state = deal.State, signatures = rows.Select(r => new { signer = r.SignerName, party = r.Party, result = r.Result, signingTime = r.SigningTime }) }));
        if (sefChanged)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "receivable_ineligible", "The signatures are valid, but SEF no longer shows the invoice as approved by the buyer.");
        }

        var failure = rows.Where(r => r.Result != "Valid").Select(r => r.Result).FirstOrDefault();
        return rows.Any(r => r.Result == "Valid") || failure is null
            ? outcome
            : Problems.Error(StatusCodes.Status422UnprocessableEntity, failure, $"No valid signature of an authorized person: {string.Join(", ", rows.Select(r => $"{r.SignerName}: {r.Result}"))}.");
    }

    /// <summary>The first rule a signature breaks, or <c>Valid</c> (task 016 requirement 3).</summary>
    private static string Judge(SignatureCheck check, string? party, string? agreedSha256) =>
        !check.IntegrityValid ? "signature_invalid"
        : !check.Qualified ? "certificate_not_qualified"
        : !check.ChainValid ? "certificate_untrusted"
        : !check.NotRevoked ? "certificate_revoked"
        : agreedSha256 is null || !string.Equals(check.SignedRevisionSha256, agreedSha256, StringComparison.OrdinalIgnoreCase) ? "document_mismatch"
        : party is null ? "signer_not_authorized"
        : "Valid";

    private static HashSet<string> Names(IReadOnlyList<string> names) => names.Select(Normalize).ToHashSet(StringComparer.Ordinal);

    /// <summary>Case and spacing differences do not matter; script differences (Cyrillic/Latin) are not resolved yet (LQ-61).</summary>
    private static string Normalize(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>
    /// The signature deadline rule (task 015 requirement 6), run by the job of task 020: a deal not signed by its
    /// deadline is cancelled by the system. Returns whether it cancelled.
    /// </summary>
    public async Task<bool> CancelIfSignatureDeadlinePassedAsync(Guid id, CancellationToken cancellationToken)
    {
        var deal = await deals.Deals.SingleAsync(d => d.Id == id, cancellationToken);
        if (!BeforeSigning.Contains(deal.State) || deal.SignatureDeadline > timeProvider.GetUtcNow())
        {
            return false;
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await CancelCoreAsync(deal, "signature_deadline_missed", actorUserId: null, ct);
                await deals.SaveChangesAsync(ct);
            },
            cancellationToken);
        return true;
    }

    /// <summary>The factor's KYB package of the seller; it needs the seller's KYB-sharing consent, which comes with KYB (task 007).</summary>
    public async Task<IResult> KybPackageAsync(Guid id, CancellationToken cancellationToken) =>
        await PartyDealAsync(id, cancellationToken) is { } deal && deal.FactorOrgId == OrganizationId
            ? Problems.Error(StatusCodes.Status409Conflict, "kyb_consent_missing", "The seller has not consented to sharing its KYB package (KYB arrives with task 007).")
            : NotFound();

    /// <summary>What the deal page shows about the steps of this task, and whose turn it is.</summary>
    public async Task<object> StepsAsync(Guid id, Guid sellerOrgId, Guid factorOrgId, DealState state, CancellationToken cancellationToken)
    {
        var cdd = await deals.CddDecisions.AsNoTracking().SingleOrDefaultAsync(d => d.DealId == id, cancellationToken);
        var contract = await deals.ContractDocuments.AsNoTracking().Where(c => c.DealId == id).OrderByDescending(c => c.UploadedAt).FirstOrDefaultAsync(cancellationToken);
        var confirmedBy = contract is null
            ? []
            : await deals.TermsConfirmations.AsNoTracking().Where(c => c.ContractDocumentId == contract.Id).Select(c => c.PartyOrgId).ToListAsync(cancellationToken);
        var waitingFor = state switch
        {
            DealState.AwaitingFactorCdd => "Factor",
            DealState.AwaitingContract when contract is null => "Factor",
            DealState.AwaitingContract => confirmedBy.Contains(sellerOrgId) ? "Factor" : confirmedBy.Contains(factorOrgId) ? "Seller" : "Both",
            DealState.AwaitingSignatures => "Both",
            _ => null,
        };
        var signatures = await deals.Signatures.AsNoTracking().Where(s => s.DealId == id).OrderBy(s => s.ValidatedAt)
            .Select(s => new { signer = s.SignerName, party = s.Party, result = s.Result, signingTime = s.SigningTime, validatedAt = s.ValidatedAt })
            .ToListAsync(cancellationToken);
        return new
        {
            signatures,
            cdd = cdd is null ? null : new { approved = cdd.Approved, reason = cdd.Reason, decidedAt = cdd.DecidedAt },
            contract = contract is null ? null : new { contractDocumentId = contract.Id, documentId = contract.DocumentId, sha256 = contract.Sha256, uploadedAt = contract.UploadedAt },
            sellerConfirmed = confirmedBy.Contains(sellerOrgId),
            factorConfirmed = confirmedBy.Contains(factorOrgId),
            waitingFor,
        };
    }

    private Task<Deal?> PartyDealAsync(Guid id, CancellationToken cancellationToken) =>
        deals.Deals.SingleOrDefaultAsync(d => d.Id == id && (d.SellerOrgId == OrganizationId || d.FactorOrgId == OrganizationId), cancellationToken);

    /// <summary>The state, Signatory and hold guards shared by the steps; null when the step may go ahead.</summary>
    private async Task<IResult?> RefusalAsync(Deal deal, DealState required, bool signatory, CancellationToken cancellationToken)
    {
        if (deal.State != required)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", $"The deal is {deal.State}; this step needs {required}.");
        }

        if (signatory && !await organizations.IsSignatoryAsync(OrganizationId, UserId, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "signatory_required", "Only a Signatory of your organization can take this step.");
        }

        if (await holds.HasActiveHoldAsync(HoldSubject.Deal, deal.Id, cancellationToken)
            || await holds.HasActiveHoldAsync(HoldSubject.Receivable, deal.ReceivableId, cancellationToken)
            || await holds.HasActiveHoldAsync(HoldSubject.Organization, deal.SellerOrgId, cancellationToken)
            || await holds.HasActiveHoldAsync(HoldSubject.Organization, deal.FactorOrgId, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status423Locked, "hold_active", "A hold blocks this step.");
        }

        return null;
    }

    private async Task CancelCoreAsync(Deal deal, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        await TransitionAsync(deal, DealState.Cancelled, reason, actorUserId, cancellationToken);
        await receivables.ReleaseAsync(deal.ReceivableId, deal.Id, $"deal_cancelled: {reason}", cancellationToken);
        await outbox.AddAsync(new DealCancelled(deal.Id, reason, deal.SellerOrgId, deal.FactorOrgId), cancellationToken);
    }

    private async Task TransitionAsync(Deal deal, DealState to, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var from = deal.State;
        deal.State = to;
        deals.StateHistory.Add(new DealStateChange
        {
            Id = SequentialGuid.NewGuid(),
            DealId = deal.Id,
            FromState = from,
            ToState = to,
            ActorUserId = actorUserId,
            At = timeProvider.GetUtcNow(),
            Reason = reason,
        });
        await audit.WriteAsync(new AuditEntry($"Deal.{to}", "Deal", deal.Id, deal.SellerOrgId,
            PreviousState: from.ToString(), NewState: to.ToString(), Reason: reason), cancellationToken);
    }

    private async Task<IResult> CommitAsync(Func<CancellationToken, Task> work, Deal deal, CancellationToken cancellationToken, Func<IResult>? result = null)
    {
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await work(ct);
                    await deals.SaveChangesAsync(ct);
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Another request changed the deal first. Reload it and try again.");
        }
        catch (DomainConflictException conflict)
        {
            return Problems.Error(conflict.Status, conflict.ErrorCode, conflict.Message);
        }

        return result?.Invoke() ?? Results.Ok(new { state = deal.State });
    }

    private static IResult NotFound() => Problems.Error(StatusCodes.Status404NotFound, "not_found", "The deal does not exist.");
}
