using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Receivables.Contracts;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Kernel;

namespace Modules.Receivables;

/// <summary><see cref="IReceivableMarket"/> and <see cref="IReceivableWriter"/> over the receivables schema; reads ignore the seller filter on purpose.</summary>
internal sealed class ReceivableMarket(
    ReceivablesDbContext receivables,
    SefStatusPoller poller,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    TimeProvider timeProvider) : IReceivableMarket, IReceivableWriter
{
    public async Task<MarketReceivable?> GetAsync(Guid receivableId, CancellationToken cancellationToken)
    {
        var receivable = await receivables.Receivables.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.Id == receivableId, cancellationToken);
        if (receivable is null)
        {
            return null;
        }

        var invoice = await receivables.Invoices.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == receivable.InvoiceId, cancellationToken);
        var debtor = await receivables.Debtors.AsNoTracking().SingleAsync(d => d.Id == receivable.DebtorId, cancellationToken);
        var evidence = await receivables.ReceivableDocuments.AsNoTracking().Where(d => d.ReceivableId == receivableId)
            .OrderBy(d => d.AttachedAt).Select(d => d.DocumentId).ToListAsync(cancellationToken);
        return new MarketReceivable(
            receivable.Id,
            receivable.SellerOrgId,
            receivable.State,
            receivable.FaceValue,
            receivable.MaturityDate,
            invoice.InvoiceNumber,
            invoice.IssueDate,
            invoice.DueDate,
            new Money(invoice.NetAmount, invoice.Currency),
            new Money(invoice.VatAmount, invoice.Currency),
            new Money(invoice.GrossAmount, invoice.Currency),
            invoice.SefStatus,
            invoice.SefStatusCheckedAt,
            debtor.Pib,
            debtor.Mb,
            debtor.LegalName,
            invoice.UblDocumentId,
            evidence);
    }

    public async Task<IReadOnlyDictionary<Guid, MarketReceivableSummary>> SummariesAsync(IReadOnlyCollection<Guid> receivableIds, CancellationToken cancellationToken) =>
        await (from receivable in receivables.Receivables.IgnoreQueryFilters().AsNoTracking()
               join invoice in receivables.Invoices.IgnoreQueryFilters().AsNoTracking() on receivable.InvoiceId equals invoice.Id
               join debtor in receivables.Debtors.AsNoTracking() on receivable.DebtorId equals debtor.Id
               where receivableIds.Contains(receivable.Id)
               select new MarketReceivableSummary(receivable.Id, debtor.LegalName, invoice.SefStatus))
            .ToDictionaryAsync(summary => summary.Id, cancellationToken);

    public async Task<SefApproval> EnsureFreshApprovalAsync(Guid receivableId, TimeSpan maxAge, CancellationToken cancellationToken)
    {
        var receivable = await receivables.Receivables.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == receivableId, cancellationToken);
        var invoice = await receivables.Invoices.IgnoreQueryFilters().SingleAsync(i => i.Id == receivable.InvoiceId, cancellationToken);
        if (invoice.SefStatusCheckedAt >= timeProvider.GetUtcNow() - maxAge)
        {
            return invoice.SefStatus == "Approved" ? SefApproval.Approved : SefApproval.NotApproved;
        }

        if (await poller.PollAsync(receivable.SellerOrgId, invoice, cancellationToken) is not { } status)
        {
            return SefApproval.Unavailable;
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await receivables.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Invoice.SefStatusObserved", "Invoice", invoice.Id, receivable.SellerOrgId, NewState: status), ct);
            },
            cancellationToken);
        return status == "Approved" ? SefApproval.Approved : SefApproval.NotApproved;
    }

    public async Task<bool> ChangeStateAsync(Guid receivableId, ReceivableState from, ReceivableState to, string reason, CancellationToken cancellationToken)
    {
        var receivable = await receivables.Receivables.IgnoreQueryFilters().SingleAsync(r => r.Id == receivableId, cancellationToken);
        if (receivable.State != from)
        {
            return false;
        }

        receivable.State = to;
        await receivables.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry($"Receivable.{to}", "Receivable", receivableId, receivable.SellerOrgId,
            PreviousState: from.ToString(), NewState: to.ToString(), Reason: reason), cancellationToken);
        return true;
    }

    public async Task<Guid?> ReceivableOfDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        await receivables.ReceivableDocuments.AsNoTracking().Where(d => d.DocumentId == documentId).Select(d => (Guid?)d.ReceivableId).FirstOrDefaultAsync(cancellationToken)
        ?? await receivables.Receivables.IgnoreQueryFilters().AsNoTracking()
            .Where(r => receivables.Invoices.IgnoreQueryFilters().Any(i => i.Id == r.InvoiceId && i.UblDocumentId == documentId))
            .OrderByDescending(r => r.CreatedAt).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task ReserveAsync(Guid receivableId, Guid dealId, CancellationToken cancellationToken)
    {
        await receivables.Database.ExecuteSqlAsync($"SELECT id FROM receivables.receivables WITH (UPDLOCK, ROWLOCK) WHERE id = {receivableId}", cancellationToken);
        var receivable = await receivables.Receivables.IgnoreQueryFilters().SingleAsync(r => r.Id == receivableId, cancellationToken);
        var invoice = await receivables.Invoices.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == receivable.InvoiceId, cancellationToken);
        if (receivable.State != ReceivableState.Listed || receivable.FaceValue > new Money(invoice.GrossAmount, invoice.Currency))
        {
            throw new DomainConflictException("receivable_ineligible", "The receivable can no longer be reserved.");
        }

        receivable.State = ReceivableState.Reserved;
        await receivables.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("Receivable.Reserved", "Receivable", receivableId, receivable.SellerOrgId,
            PreviousState: nameof(ReceivableState.Listed), NewState: nameof(ReceivableState.Reserved), Related: [new AuditReference("Deal", dealId)]), cancellationToken);
    }

    public async Task ReleaseAsync(Guid receivableId, Guid dealId, string reason, CancellationToken cancellationToken)
    {
        await receivables.Database.ExecuteSqlAsync($"SELECT id FROM receivables.receivables WITH (UPDLOCK, ROWLOCK) WHERE id = {receivableId}", cancellationToken);
        var receivable = await receivables.Receivables.IgnoreQueryFilters().SingleAsync(r => r.Id == receivableId, cancellationToken);
        if (receivable.State != ReceivableState.Reserved)
        {
            throw new DomainConflictException("conflict_state", "The receivable is not reserved.");
        }

        receivable.State = ReceivableState.Verified;
        await receivables.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("Receivable.Released", "Receivable", receivableId, receivable.SellerOrgId,
            PreviousState: nameof(ReceivableState.Reserved), NewState: nameof(ReceivableState.Verified), Reason: reason, Related: [new AuditReference("Deal", dealId)]), cancellationToken);
    }

    public async Task MoveForDealAsync(Guid receivableId, Guid dealId, ReceivableState from, ReceivableState to, CancellationToken cancellationToken)
    {
        await receivables.Database.ExecuteSqlAsync($"SELECT id FROM receivables.receivables WITH (UPDLOCK, ROWLOCK) WHERE id = {receivableId}", cancellationToken);
        var receivable = await receivables.Receivables.IgnoreQueryFilters().SingleAsync(r => r.Id == receivableId, cancellationToken);
        if (receivable.State != from)
        {
            throw new DomainConflictException("conflict_state", $"The receivable is {receivable.State}, not {from}.");
        }

        receivable.State = to;
        await receivables.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry($"Receivable.{to}", "Receivable", receivableId, receivable.SellerOrgId,
            PreviousState: from.ToString(), NewState: to.ToString(), Related: [new AuditReference("Deal", dealId)]), cancellationToken);
    }

    public async Task AssignAsync(Guid receivableId, Guid dealId, CancellationToken cancellationToken)
    {
        await receivables.Database.ExecuteSqlAsync($"SELECT id FROM receivables.receivables WITH (UPDLOCK, ROWLOCK) WHERE id = {receivableId}", cancellationToken);
        var receivable = await receivables.Receivables.IgnoreQueryFilters().SingleAsync(r => r.Id == receivableId, cancellationToken);
        if (receivable.State != ReceivableState.Reserved)
        {
            throw new DomainConflictException("receivable_ineligible", "The receivable is not reserved for this deal.");
        }

        receivable.State = ReceivableState.Assigned;
        await receivables.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("Receivable.Assigned", "Receivable", receivableId, receivable.SellerOrgId,
            PreviousState: nameof(ReceivableState.Reserved), NewState: nameof(ReceivableState.Assigned), Related: [new AuditReference("Deal", dealId)]), cancellationToken);
    }
}
