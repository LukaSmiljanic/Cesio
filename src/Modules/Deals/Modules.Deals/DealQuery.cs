using Microsoft.EntityFrameworkCore;
using Modules.Deals.Contracts;
using Modules.Deals.Persistence;

namespace Modules.Deals;

/// <summary><see cref="IDealQuery"/> (task 019): who must hear about a seller's payout account changes.</summary>
internal sealed class DealQuery(DealsDbContext deals) : IDealQuery
{
    private static readonly DealState[] Ended = [DealState.Closed, DealState.Cancelled, DealState.Terminated];

    public async Task<IReadOnlyList<Guid>> FactorsWithOpenDealsAsync(Guid sellerOrgId, CancellationToken cancellationToken) =>
        await deals.Deals.AsNoTracking()
            .Where(d => d.SellerOrgId == sellerOrgId && !Ended.Contains(d.State))
            .Select(d => d.FactorOrgId)
            .Distinct()
            .ToListAsync(cancellationToken);
}
