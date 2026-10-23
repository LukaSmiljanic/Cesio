using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Deals.Contracts;
using Modules.Deals.Persistence;
using Modules.Documents.Contracts;
using Modules.Receivables.Contracts;
using Shared.Kernel;

namespace Modules.Deals;

public sealed record CefRegistrationRequest(string? Reference, Guid EvidenceDocumentId);

public sealed record DebtorNotificationRequest(string? Channel, DateTimeOffset SentAt, Guid EvidenceDocumentId);

public sealed record FundingReportRequest(Money? Amount, DateOnly ValueDate, string? PaymentReference, Guid PayeeBankAccountId);

public sealed record FundingConfirmationRequest(bool Received, string? Reason);

public sealed record OutcomeRequest(OutcomeType? Type, DateOnly Date, Money? AmountCollected);

public sealed record TerminateDealRequest(string? Reason, IReadOnlyList<Guid>? EvidenceDocumentIds, Guid SellerConfirmationDocumentId, Guid FactorConfirmationDocumentId);

/// <summary>
/// The deal after signing (task 017, product-spec.md §6.5 steps 4–8): CEF and debtor-notification evidence, the
/// factor's funding report, the seller's confirmation (never automatic), the outcome, and the staff termination of a
/// signed deal before funding.
/// </summary>
internal sealed partial class DealStepService
{
    /// <summary>LQ-17: whether a Pending CEF registration blocks funding; default false.</summary>
    public const string BlockFundingUntilCefRegistered = "cef.blockFundingUntilRegistered";

    private static readonly string[] DebtorNotificationChannels = ["Email", "Letter", "Courier", "Other"];

    /// <summary>The seller's usable payout accounts, unmasked for the factor at the funding step only; access logged.</summary>
    public async Task<IResult> FundingAccountsAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal || deal.FactorOrgId != OrganizationId)
        {
            return NotFound();
        }

        if (deal.State != DealState.Signed)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The payout account is shown only while the deal awaits funding.");
        }

        var accounts = await bankAccounts.UsableForFundingAsync(deal.SellerOrgId, cancellationToken);
        await unitOfWork.ExecuteInTransactionAsync(
            ct => accessLog.RecordAsync(new AccessLogEntry("Deal.PayoutAccountViewed", "Deal", id, OrganizationId), ct),
            cancellationToken);
        return Results.Ok(accounts.Select(a => new { id = a.Id, accountNumber = a.AccountNumber.Value, bankName = a.BankName, currency = a.Currency.Code }));
    }

    public async Task<IResult> RecordCefRegistrationAsync(Guid id, CefRegistrationRequest body, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal || deal.SellerOrgId != OrganizationId)
        {
            return NotFound();
        }

        var registration = await deals.CefRegistrations.SingleOrDefaultAsync(c => c.DealId == id, cancellationToken);
        if (registration is not { Status: CefStatus.Pending })
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "CEF registration is recorded only while it is Pending (the CEF is operational).");
        }

        var reference = body.Reference?.Trim();
        if (string.IsNullOrEmpty(reference) || reference.Length > 100)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give the CEF registration reference (at most 100 characters).");
        }

        if (await EvidenceErrorAsync(body.EvidenceDocumentId, cancellationToken) is { } error)
        {
            return error;
        }

        registration.Status = CefStatus.Registered;
        registration.Reference = reference;
        registration.EvidenceDocumentId = body.EvidenceDocumentId;
        registration.RecordedBy = UserId;
        registration.RegisteredAt = timeProvider.GetUtcNow();
        return await CommitAsync(
            ct => audit.WriteAsync(new AuditEntry("Deal.CefRegistered", "Deal", id, OrganizationId, EvidenceDocumentIds: [body.EvidenceDocumentId],
                Changes: new Dictionary<string, string?> { ["reference"] = reference }), ct),
            deal,
            cancellationToken,
            () => Results.Ok(new { status = registration.Status }));
    }

    /// <summary>Either party records the debtor notification (MVP; who must notify is LQ-14).</summary>
    public async Task<IResult> RecordDebtorNotificationAsync(Guid id, DebtorNotificationRequest body, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal)
        {
            return NotFound();
        }

        if (deal.State is not (DealState.Signed or DealState.FundingReported or DealState.Funded))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The debtor is notified of a signed assignment.");
        }

        if (body.Channel is null || !DebtorNotificationChannels.Contains(body.Channel) || body.SentAt > timeProvider.GetUtcNow())
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", $"Give the channel ({string.Join(", ", DebtorNotificationChannels)}) and when it was sent (not in the future).");
        }

        if (await EvidenceErrorAsync(body.EvidenceDocumentId, cancellationToken) is { } error)
        {
            return error;
        }

        deals.DebtorNotifications.Add(new DebtorNotification
        {
            Id = SequentialGuid.NewGuid(),
            DealId = id,
            Channel = body.Channel,
            SentAt = body.SentAt,
            EvidenceDocumentId = body.EvidenceDocumentId,
            RecordedByOrgId = OrganizationId,
            RecordedBy = UserId,
            RecordedAt = timeProvider.GetUtcNow(),
        });
        return await CommitAsync(
            ct => audit.WriteAsync(new AuditEntry("Deal.DebtorNotified", "Deal", id, OrganizationId, EvidenceDocumentIds: [body.EvidenceDocumentId],
                Changes: new Dictionary<string, string?> { ["channel"] = body.Channel, ["sentAt"] = body.SentAt.ToString("O", CultureInfo.InvariantCulture) }), ct),
            deal,
            cancellationToken,
            () => Results.Ok(new { state = deal.State }));
    }

    /// <summary>
    /// The factor reports the payment (api.md §3.8, INV-F2): to a usable account of the seller; an amount other than the
    /// purchase price is accepted but opens a FundingDiscrepancy dispute, which holds the deal.
    /// </summary>
    public async Task<IResult> ReportFundingAsync(Guid id, FundingReportRequest body, DateTimeOffset stepUpAt, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal || deal.FactorOrgId != OrganizationId)
        {
            return NotFound();
        }

        if (await RefusalAsync(deal, DealState.Signed, signatory: true, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var reference = body.PaymentReference?.Trim();
        if (body.Amount is not { } amount || amount.Currency != deal.FaceValue.Currency || amount.Amount <= 0
            || string.IsNullOrEmpty(reference) || reference.Length > 100
            || body.ValueDate > BusinessDays.Add(today, 1))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed",
                "Give a positive amount in the deal's currency, the payment reference (at most 100 characters) and a value date at most one business day ahead.");
        }

        var account = (await bankAccounts.UsableForFundingAsync(deal.SellerOrgId, cancellationToken)).SingleOrDefault(a => a.Id == body.PayeeBankAccountId);
        if (account is null || account.Currency != amount.Currency)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "account_not_usable", "Pay to a Verified payout account of the seller that is out of its cool-down (INV-F2).");
        }

        if (await parameters.GetAsync(BlockFundingUntilCefRegistered, false, cancellationToken)
            && await deals.CefRegistrations.AnyAsync(c => c.DealId == id && c.Status == CefStatus.Pending, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "cef_not_registered", "Funding waits for the CEF registration (LQ-17).");
        }

        var purchasePrice = new Money(deal.PurchasePriceAmount, deal.FaceValue.Currency);
        var matches = amount.Round() == purchasePrice.Round();
        deals.FundingReports.Add(new FundingReport
        {
            Id = SequentialGuid.NewGuid(),
            DealId = id,
            Amount = amount,
            ValueDate = body.ValueDate,
            PaymentReference = reference,
            PayeeBankAccountId = account.Id,
            PayeeAccountNumber = account.AccountNumber.Value,
            ReportedBy = UserId,
            ReportedAt = timeProvider.GetUtcNow(),
            StepUpAt = stepUpAt,
        });
        if (!matches)
        {
            OpenDispute(deal, Dispute.FundingDiscrepancy, $"Reported {amount.Round()}, purchase price {purchasePrice.Round()}.", openedBy: null);
        }

        return await CommitAsync(
            async ct =>
            {
                if (!matches)
                {
                    await audit.WriteAsync(new AuditEntry("Dispute.Opened", "Deal", id, deal.SellerOrgId, Reason: Dispute.FundingDiscrepancy), ct);
                }

                await TransitionAsync(deal, DealState.FundingReported, matches ? "funding_reported" : "funding_reported_amount_differs", UserId, ct,
                    new Dictionary<string, string?>
                    {
                        ["amount"] = amount.Round().ToString(),
                        ["valueDate"] = body.ValueDate.ToString("O", CultureInfo.InvariantCulture),
                        ["payeeAccount"] = account.AccountNumber.ToString(),
                        ["stepUpAt"] = stepUpAt.ToString("O", CultureInfo.InvariantCulture),
                    });
                await outbox.AddAsync(new DealFundingReported(id, deal.SellerOrgId, deal.FactorOrgId, matches), ct);
            },
            deal,
            cancellationToken,
            () => Results.Ok(new { state = deal.State, amountMatches = matches }));
    }

    /// <summary>The seller confirms receipt (→ Funded, fee due via DealFunded) or reports a discrepancy (dispute + hold).</summary>
    public async Task<IResult> ConfirmFundingAsync(Guid id, FundingConfirmationRequest body, DateTimeOffset stepUpAt, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal || deal.SellerOrgId != OrganizationId)
        {
            return NotFound();
        }

        var reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();
        if ((!body.Received && reason is null) || reason is { Length: > 400 })
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Say what is wrong with the payment (at most 400 characters).");
        }

        if (await RefusalAsync(deal, DealState.FundingReported, signatory: true, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        deals.FundingConfirmations.Add(new FundingConfirmation
        {
            Id = SequentialGuid.NewGuid(),
            DealId = id,
            Received = body.Received,
            Reason = reason,
            ConfirmedBy = UserId,
            ConfirmedAt = timeProvider.GetUtcNow(),
            StepUpAt = stepUpAt,
        });
        if (!body.Received)
        {
            OpenDispute(deal, Dispute.FundingDiscrepancy, reason, UserId);
        }

        return await CommitAsync(
            async ct =>
            {
                if (body.Received)
                {
                    await TransitionAsync(deal, DealState.Funded, "seller_confirmed_receipt", UserId, ct);
                    await receivables.MoveForDealAsync(deal.ReceivableId, id, ReceivableState.Assigned, ReceivableState.Funded, ct);
                    var currency = deal.FaceValue.Currency;
                    await outbox.AddAsync(new DealFunded(id, deal.SellerOrgId, deal.FactorOrgId, deal.FeeScheduleVersion,
                        new Money(deal.PlatformFeeAmount, currency).Round(), new Money(deal.SellerFeeAmount, currency).Round()), ct);
                }
                else
                {
                    await audit.WriteAsync(new AuditEntry("Dispute.Opened", "Deal", id, OrganizationId, Reason: $"{Dispute.FundingDiscrepancy}: {reason}"), ct);
                }

                await audit.WriteAsync(new AuditEntry(body.Received ? "Deal.FundingConfirmed" : "Deal.FundingDisputed", "Deal", id, OrganizationId,
                    Changes: new Dictionary<string, string?> { ["stepUpAt"] = stepUpAt.ToString("O", CultureInfo.InvariantCulture) }), ct);
            },
            deal,
            cancellationToken,
            () => Results.Ok(new { state = deal.State }));
    }

    /// <summary>The factor records the outcome of a funded deal (api.md §3.10) → Closed; the receivable reaches its terminal state.</summary>
    public async Task<IResult> RecordOutcomeAsync(Guid id, OutcomeRequest body, DateTimeOffset stepUpAt, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal || deal.FactorOrgId != OrganizationId)
        {
            return NotFound();
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (body.Type is not { } type || body.Date > today
            || (body.AmountCollected is { } collected && (collected.Currency != deal.FaceValue.Currency || collected.Amount < 0)))
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give the outcome type, a date not in the future and, optionally, the amount collected in the deal's currency.");
        }

        if (await RefusalAsync(deal, DealState.Funded, signatory: true, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        deals.Outcomes.Add(new Outcome
        {
            Id = SequentialGuid.NewGuid(),
            DealId = id,
            Type = type,
            Date = body.Date,
            AmountCollectedAmount = body.AmountCollected?.Amount,
            ReportedBy = UserId,
            ReportedAt = timeProvider.GetUtcNow(),
        });
        var terminal = type switch
        {
            OutcomeType.Settled => ReceivableState.Settled,
            OutcomeType.RecourseExercised => ReceivableState.RecourseReturned,
            _ => ReceivableState.Defaulted,
        };
        return await CommitAsync(
            async ct =>
            {
                await audit.WriteAsync(new AuditEntry("Deal.OutcomeRecorded", "Deal", id, OrganizationId,
                    Changes: new Dictionary<string, string?>
                    {
                        ["type"] = type.ToString(),
                        ["date"] = body.Date.ToString("O", CultureInfo.InvariantCulture),
                        ["amountCollected"] = body.AmountCollected?.Round().ToString(),
                        ["stepUpAt"] = stepUpAt.ToString("O", CultureInfo.InvariantCulture),
                    }), ct);
                await TransitionAsync(deal, DealState.Closed, $"outcome_{type}", UserId, ct);
                await receivables.MoveForDealAsync(deal.ReceivableId, id, ReceivableState.Funded, terminal, ct);
                await outbox.AddAsync(new DealClosed(id, type), ct);
            },
            deal,
            cancellationToken,
            () => Results.Ok(new { state = deal.State }));
    }

    /// <summary>
    /// Staff end a signed deal before funding (INV-R6), with evidence and both parties' written confirmations; the
    /// receivable returns to Verified. A funded deal can never be terminated.
    /// </summary>
    public async Task<IResult> TerminateAsync(Guid id, TerminateDealRequest body, CancellationToken cancellationToken)
    {
        if (await deals.Deals.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is not { } deal)
        {
            return NotFound();
        }

        if (deal.State is not (DealState.Signed or DealState.FundingReported))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Only a signed deal that is not funded yet can be terminated.");
        }

        var reason = body.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 1000 || body.EvidenceDocumentIds is not { Count: > 0 })
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give the reason and at least one evidence document.");
        }

        foreach (var (documentId, owner) in body.EvidenceDocumentIds.Select(d => (d, (Guid?)null))
            .Append((body.SellerConfirmationDocumentId, deal.SellerOrgId))
            .Append((body.FactorConfirmationDocumentId, deal.FactorOrgId)))
        {
            if (await documents.GetAsync(documentId, cancellationToken) is not { ScanStatus: ScanStatus.Clean } document
                || (owner is { } party ? document.OwnerOrgId != party : document.OwnerOrgId != deal.SellerOrgId && document.OwnerOrgId != deal.FactorOrgId))
            {
                return Problems.Error(StatusCodes.Status422UnprocessableEntity, "evidence_missing",
                    "Every evidence document must be a clean document of a party; each party's confirmation must be its own document.");
            }
        }

        var from = deal.State;
        return await CommitAsync(
            async ct =>
            {
                await TransitionAsync(deal, DealState.Terminated, reason, UserId, ct);
                await receivables.MoveForDealAsync(deal.ReceivableId, id, ReceivableState.Assigned, ReceivableState.Verified, ct);
                await audit.WriteAsync(new AuditEntry("Deal.TerminatedByStaff", "Deal", id, deal.SellerOrgId, PreviousState: from.ToString(),
                    NewState: nameof(DealState.Terminated), Reason: reason,
                    EvidenceDocumentIds: [.. body.EvidenceDocumentIds, body.SellerConfirmationDocumentId, body.FactorConfirmationDocumentId]), ct);
                await outbox.AddAsync(new DealTerminated(id, deal.ReceivableId, reason), ct);
            },
            deal,
            cancellationToken,
            () => Results.Ok(new { state = deal.State }));
    }

    /// <summary>At signing: Pending when the CEF is operational, otherwise NotApplicable with a dated note (ADR-019).</summary>
    private async Task CreateCefRegistrationAsync(Deal deal, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var operational = await cef.IsOperationalAsync(now, cancellationToken);
        deals.CefRegistrations.Add(new CefRegistration
        {
            DealId = deal.Id,
            Status = operational ? CefStatus.Pending : CefStatus.NotApplicable,
            Note = operational ? null : string.Create(CultureInfo.InvariantCulture, $"CEF not operational on {now:yyyy-MM-dd}"),
            CreatedAt = now,
        });
        await audit.WriteAsync(new AuditEntry("Deal.CefRegistrationCreated", "Deal", deal.Id, deal.SellerOrgId,
            NewState: operational ? nameof(CefStatus.Pending) : nameof(CefStatus.NotApplicable)), cancellationToken);
    }

    private void OpenDispute(Deal deal, string kind, string? reason, Guid? openedBy) =>
        deals.Disputes.Add(new Dispute
        {
            Id = SequentialGuid.NewGuid(),
            DealId = deal.Id,
            Kind = kind,
            Status = Dispute.Open,
            Reason = reason,
            OpenedBy = openedBy,
            OpenedAt = timeProvider.GetUtcNow(),
        });

    private async Task<IResult?> EvidenceErrorAsync(Guid documentId, CancellationToken cancellationToken) =>
        await documents.GetAsync(documentId, cancellationToken) is { ScanStatus: ScanStatus.Clean } document && document.OwnerOrgId == OrganizationId
            ? null
            : Problems.Error(StatusCodes.Status422UnprocessableEntity, "evidence_missing", "Attach a clean evidence document of your organization.");
}
