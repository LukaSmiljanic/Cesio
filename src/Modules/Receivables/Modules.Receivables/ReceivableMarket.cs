using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Receivables.Contracts;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Kernel;

namespace Modules.Receivables;

/// <summary><see cref="IReceivableMarket"/> over the receivables schema; reads ignore the seller filter on purpose.</summary>
internal sealed class ReceivableMarket(
    ReceivablesDbContext receivables,
    SefStatusPoller poller,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    TimeProvider timeProvider) : IReceivableMarket
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
}
