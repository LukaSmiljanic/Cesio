using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Notifications.Contracts;
using Modules.Receivables.Contracts;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Kernel;

namespace Modules.Receivables;

/// <summary>
/// The receivable jobs of task 020: maturity ineligibility and SEF monitoring. Each item has its own transaction, so one
/// failing item does not stop the batch; every system transition is audited with a reason code.
/// </summary>
internal sealed class ReceivableJobs(
    ReceivablesDbContext receivables,
    SefStatusPoller poller,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IBusinessParameters parameters,
    TimeProvider timeProvider)
{
    public const int BatchSize = 100;

    /// <summary>SEF statuses after which the invoice can no longer be financed (integrations.md §1).</summary>
    public static readonly string[] Withdrawn = ["Cancelled", "Storno", "Deleted", "Rejected"];

    private static readonly ReceivableState[] Monitored =
        [ReceivableState.Verified, ReceivableState.Listed, ReceivableState.Reserved, ReceivableState.Assigned, ReceivableState.Funded];

    /// <summary>A Reserved receivable is left to Deals, which cancels the deal first and then marks it Ineligible.</summary>
    private static readonly ReceivableState[] BeforeAssignment = [ReceivableState.Verified, ReceivableState.Listed];

    /// <summary>Daily: Verified or Listed receivables too close to maturity to list become Ineligible (their listing is withdrawn by Marketplace).</summary>
    public async Task<int> MarkMaturityIneligibleAsync(CancellationToken cancellationToken)
    {
        var minDays = await parameters.GetAsync(ReceivableParameters.MinDaysAtListing, 15, cancellationToken);
        var limit = BusinessDayCalendar.Today(timeProvider).AddDays(minDays);
        var due = await receivables.Receivables.IgnoreQueryFilters()
            .Where(r => (r.State == ReceivableState.Verified || r.State == ReceivableState.Listed) && r.MaturityDate < limit)
            .OrderBy(r => r.MaturityDate)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var count = 0;
        foreach (var receivable in due)
        {
            count += await TryAsync(() => IneligibleAsync(receivable, "maturity_too_close", cancellationToken)) ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// Daily: re-polls SEF for monitored receivables, oldest check first. A withdrawn invoice before assignment makes the
    /// receivable Ineligible; at or after assignment the state stays and Deals puts the deal on hold (a storno is never
    /// a rollback). The seller is told either way.
    /// </summary>
    public async Task<int> MonitorSefAsync(CancellationToken cancellationToken)
    {
        var rows = await (from r in receivables.Receivables.IgnoreQueryFilters()
                          join i in receivables.Invoices.IgnoreQueryFilters() on r.InvoiceId equals i.Id
                          where Monitored.Contains(r.State)
                          select new { Receivable = r, Invoice = i })
            // A random sample: receivables whose SEF cannot be asked keep their old check time (it must not look
            // fresh, INV-R4), so ordering by it would retry the same ones forever.
            .OrderBy(_ => Guid.NewGuid())
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        var count = 0;
        foreach (var row in rows)
        {
            var previous = row.Invoice.SefStatus;
            var ok = await TryAsync(async () =>
            {
                if (await poller.PollAsync(row.Receivable.SellerOrgId, row.Invoice, cancellationToken) is not { } status)
                {
                    // No working key or SEF unavailable: acceptance and listing already fail closed on a stale status (INV-R4).
                    return;
                }

                var withdrawn = Withdrawn.Contains(status) && !Withdrawn.Contains(previous);
                await unitOfWork.ExecuteInTransactionAsync(
                    async ct =>
                    {
                        if (withdrawn && BeforeAssignment.Contains(row.Receivable.State))
                        {
                            await IneligibleCoreAsync(row.Receivable, "sef_status_withdrawn", ct);
                        }
                        else
                        {
                            await receivables.SaveChangesAsync(ct);
                        }

                        await audit.WriteAsync(new AuditEntry("Invoice.SefStatusObserved", "Invoice", row.Invoice.Id, row.Receivable.SellerOrgId, PreviousState: previous, NewState: status), ct);
                        if (withdrawn)
                        {
                            await outbox.AddAsync(new ReceivableSefStatusChanged(row.Receivable.Id, row.Receivable.SellerOrgId, status, row.Receivable.State), ct);
                            await outbox.AddAsync(new OrganizationNotificationRequested(row.Receivable.SellerOrgId, NotificationTypes.SefStatusChanged,
                                $"/receivables/{row.Receivable.Id}", null, $"sef-status:{row.Receivable.Id}:{status}"), ct);
                        }
                    },
                    cancellationToken);
            });
            count += ok ? 1 : 0;
        }

        return count;
    }

    private async Task IneligibleAsync(Receivable receivable, string reason, CancellationToken cancellationToken) =>
        await unitOfWork.ExecuteInTransactionAsync(ct => IneligibleCoreAsync(receivable, reason, ct), cancellationToken);

    private async Task IneligibleCoreAsync(Receivable receivable, string reason, CancellationToken cancellationToken)
    {
        var previous = receivable.State;
        receivable.State = ReceivableState.Ineligible;
        await receivables.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("Receivable.BecameIneligible", "Receivable", receivable.Id, receivable.SellerOrgId,
            PreviousState: previous.ToString(), NewState: nameof(ReceivableState.Ineligible), Reason: reason), cancellationToken);
        await outbox.AddAsync(new ReceivableBecameIneligible(receivable.Id, receivable.SellerOrgId, previous, reason), cancellationToken);
    }

    /// <summary>One item: a failure (e.g. a concurrent change) is skipped and retried by the next run.</summary>
    private async Task<bool> TryAsync(Func<Task> item)
    {
        try
        {
            await item();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            receivables.ChangeTracker.Clear();
            return false;
        }
    }
}
