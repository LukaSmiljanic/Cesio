using Microsoft.EntityFrameworkCore;
using Modules.Receivables.Contracts;
using Modules.Receivables.Persistence;

namespace Modules.Receivables;

internal sealed class ReceivableStats(ReceivablesDbContext receivables) : IReceivableStats
{
    public async Task<IReadOnlyDictionary<ReceivableState, int>> CountByStateAsync(Guid? sellerOrgId, CancellationToken cancellationToken) =>
        (await receivables.Receivables.IgnoreQueryFilters().AsNoTracking()
            .Where(r => sellerOrgId == null || r.SellerOrgId == sellerOrgId)
            .GroupBy(r => r.State)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(x => x.Key, x => x.Count);

    public Task<int> ReviewQueueAsync(CancellationToken cancellationToken) =>
        receivables.Receivables.IgnoreQueryFilters().CountAsync(r => r.State == ReceivableState.PendingVerification && r.ReviewRequired, cancellationToken);

    public Task<int> ImportedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        receivables.Receivables.IgnoreQueryFilters().CountAsync(r => r.CreatedAt >= since, cancellationToken);
}
