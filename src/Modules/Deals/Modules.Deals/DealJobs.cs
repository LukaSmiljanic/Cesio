using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Deals.Contracts;
using Modules.Deals.Persistence;
using Modules.Notifications.Contracts;
using Modules.Receivables.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Kernel;

namespace Modules.Deals;

/// <summary>
/// Deal jobs and system reactions of task 020: the signature deadline, funding follow-up, overdue outcomes, a SEF
/// storno (before signing the deal is cancelled; at or after assignment it is held, never rolled back), and INV-A2 for
/// a factor whose authorization ended.
/// </summary>
internal sealed class DealJobs(
    DealsDbContext deals,
    DealStepService steps,
    IReceivableMarket receivables,
    IHoldWriter holds,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IBusinessParameters parameters,
    TimeProvider timeProvider) :
    IIntegrationEventConsumer<ReceivableSefStatusChanged>,
    IIntegrationEventConsumer<FactorAuthorizationRevoked>,
    IIntegrationEventConsumer<FactorAuthorizationExpired>
{
    public const string SefConsumerName = "deals.receivable-sef-status";

    public const string RevokedConsumerName = "deals.factor-authorization-revoked";

    public const string ExpiredConsumerName = "deals.factor-authorization-expired";

    public const string FundingAlertBusinessDaysParameter = "deals.fundingConfirmationAlertBusinessDays";

    public const int OutcomeOverdueDays = 30;

    private const int BatchSize = 100;

    private static readonly DealState[] BeforeSigning = [DealState.AwaitingFactorCdd, DealState.AwaitingContract, DealState.AwaitingSignatures];

    /// <summary>Every 15 minutes: deals past the signature deadline are cancelled; deadlines within 24 h get one reminder to both parties.</summary>
    public async Task<int> SignatureDeadlinesAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await deals.Deals.AsNoTracking().Where(d => BeforeSigning.Contains(d.State) && d.SignatureDeadline <= now).Select(d => d.Id).Take(BatchSize).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var id in due)
        {
            count += await steps.CancelIfSignatureDeadlinePassedAsync(id, cancellationToken) ? 1 : 0;
            deals.ChangeTracker.Clear();
        }

        var soon = await deals.Deals.AsNoTracking()
            .Where(d => BeforeSigning.Contains(d.State) && d.SignatureDeadline > now && d.SignatureDeadline <= now.AddHours(24))
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var deal in soon)
        {
            await RemindBothAsync(deal, $"signature-deadline:{deal.Id}", cancellationToken);
        }

        return count;
    }

    /// <summary>
    /// Daily: a funding report without the seller's confirmation after N business days raises an Operations alert; a
    /// Signed deal past its funding deadline reminds both parties once.
    /// </summary>
    public async Task<int> FundingFollowUpAsync(CancellationToken cancellationToken)
    {
        var today = BusinessDayCalendar.Today(timeProvider);
        var days = await parameters.GetAsync(FundingAlertBusinessDaysParameter, 5, cancellationToken);
        // Reports at least N business days old (calendar days are a lower bound), oldest first.
        var oldest = timeProvider.GetUtcNow().AddDays(-days);
        var reported = await (from d in deals.Deals.AsNoTracking()
                              join r in deals.FundingReports.AsNoTracking() on d.Id equals r.DealId
                              where d.State == DealState.FundingReported && r.ReportedAt <= oldest
                              orderby r.ReportedAt
                              select new { d.Id, d.SellerOrgId, r.ReportedAt })
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var count = 0;
        // One alert per report: on the first business day after the confirmation was due.
        foreach (var report in reported.Where(r => BusinessDays.Add(BusinessDays.Add(BusinessDayCalendar.DateOf(r.ReportedAt), days), 1) == today))
        {
            await unitOfWork.ExecuteInTransactionAsync(
                ct => audit.WriteAsync(new AuditEntry("Operations.Alert", "Deal", report.Id, report.SellerOrgId, Reason: "funding_confirmation_overdue"), ct),
                cancellationToken);
            count++;
        }

        var late = await deals.Deals.AsNoTracking().Where(d => d.State == DealState.Signed && d.FundingDeadline < today).Take(BatchSize).ToListAsync(cancellationToken);
        foreach (var deal in late)
        {
            await RemindBothAsync(deal, $"funding-deadline:{deal.Id}", cancellationToken);
            count++;
        }

        return count;
    }

    /// <summary>Daily: Funded deals past maturity + 30 days without an outcome remind the factor once.</summary>
    public async Task<int> OutcomeOverdueAsync(CancellationToken cancellationToken)
    {
        var today = BusinessDayCalendar.Today(timeProvider);
        var funded = await deals.Deals.AsNoTracking()
            .Where(d => d.State == DealState.Funded && !deals.Outcomes.Any(o => o.DealId == d.Id))
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var count = 0;
        foreach (var deal in funded)
        {
            if (await receivables.GetAsync(deal.ReceivableId, cancellationToken) is { } receivable && receivable.MaturityDate.AddDays(OutcomeOverdueDays) < today)
            {
                await unitOfWork.ExecuteInTransactionAsync(
                    ct => outbox.AddAsync(new OrganizationNotificationRequested(deal.FactorOrgId, NotificationTypes.Reminder, $"/deals/{deal.Id}", deal.Reference, $"outcome-overdue:{deal.Id}"), ct),
                    cancellationToken);
                count++;
            }
        }

        return count;
    }

    /// <summary>SEF withdrew the invoice: a deal before signing is cancelled (the receivable then Ineligible); a later one is held for compliance.</summary>
    public async Task HandleAsync(ReceivableSefStatusChanged integrationEvent, Guid eventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var deal = await deals.Deals.Where(d => d.ReceivableId == integrationEvent.ReceivableId && d.State != DealState.Cancelled && d.State != DealState.Terminated)
            .OrderByDescending(d => d.AcceptedAt).FirstOrDefaultAsync(cancellationToken);
        if (deal is null)
        {
            return;
        }

        if (BeforeSigning.Contains(deal.State))
        {
            await steps.CancelBeforeSigningAsync(deal, "sef_status_withdrawn", cancellationToken);
            await receivables.ChangeStateAsync(deal.ReceivableId, ReceivableState.Verified, ReceivableState.Ineligible, "sef_status_withdrawn", cancellationToken);
            return;
        }

        if (deal.State is DealState.Signed or DealState.FundingReported or DealState.Funded)
        {
            await holds.PlaceSystemHoldAsync(HoldSubject.Deal, deal.Id, HoldKind.FraudInvestigation, $"sef_{integrationEvent.SefStatus.ToLowerInvariant()}_after_assignment", deal.SellerOrgId, cancellationToken);
            await audit.WriteAsync(new AuditEntry("Compliance.Alert", "Deal", deal.Id, deal.SellerOrgId, Reason: "sef_status_withdrawn_after_assignment"), cancellationToken);
            foreach (var org in new[] { deal.SellerOrgId, deal.FactorOrgId })
            {
                await outbox.AddAsync(new OrganizationNotificationRequested(org, NotificationTypes.Hold, $"/deals/{deal.Id}", deal.Reference), cancellationToken);
            }
        }
    }

    public Task HandleAsync(FactorAuthorizationRevoked integrationEvent, Guid eventId, CancellationToken cancellationToken) =>
        HoldFactorDealsAsync(integrationEvent?.FactorOrgId ?? throw new ArgumentNullException(nameof(integrationEvent)), "factor_authorization_revoked", cancellationToken);

    public Task HandleAsync(FactorAuthorizationExpired integrationEvent, Guid eventId, CancellationToken cancellationToken) =>
        HoldFactorDealsAsync(integrationEvent?.FactorOrgId ?? throw new ArgumentNullException(nameof(integrationEvent)), "factor_authorization_expired", cancellationToken);

    /// <summary>INV-A2: the factor's deals before Signed are frozen for compliance review.</summary>
    private async Task HoldFactorDealsAsync(Guid factorOrgId, string reason, CancellationToken cancellationToken)
    {
        foreach (var deal in await deals.Deals.AsNoTracking().Where(d => d.FactorOrgId == factorOrgId && BeforeSigning.Contains(d.State)).ToListAsync(cancellationToken))
        {
            await holds.PlaceSystemHoldAsync(HoldSubject.Deal, deal.Id, HoldKind.ComplianceHold, reason, deal.FactorOrgId, cancellationToken);
        }
    }

    private async Task RemindBothAsync(Deal deal, string key, CancellationToken cancellationToken) =>
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await outbox.AddAsync(new OrganizationNotificationRequested(deal.SellerOrgId, NotificationTypes.Reminder, $"/deals/{deal.Id}", deal.Reference, $"{key}:seller"), ct);
                await outbox.AddAsync(new OrganizationNotificationRequested(deal.FactorOrgId, NotificationTypes.Reminder, $"/deals/{deal.Id}", deal.Reference, $"{key}:factor"), ct);
            },
            cancellationToken);
}
