using Microsoft.EntityFrameworkCore;
using Modules.Marketplace.Contracts;
using Modules.Marketplace.Persistence;
using Shared.Kernel;

namespace Modules.Marketplace;

internal sealed class MarketplaceStats(MarketplaceDbContext marketplace) : IMarketplaceStats
{
    public async Task<IReadOnlyList<OpenListingStat>> OpenListingsAsync(Guid sellerOrgId, CancellationToken cancellationToken)
    {
        var listings = await marketplace.Listings.AsNoTracking().Where(l => l.SellerOrgId == sellerOrgId && l.State == ListingState.Open).OrderBy(l => l.ClosesAt).ToListAsync(cancellationToken);
        var ids = listings.Select(l => l.Id).ToList();
        var offers = await marketplace.Offers.AsNoTracking().Where(o => ids.Contains(o.ListingId) && o.State == OfferState.Active).ToListAsync(cancellationToken);
        return listings.Select(l =>
        {
            var mine = offers.Where(o => o.ListingId == l.Id).ToList();
            var best = mine.Select(o => o.PurchasePrice.Amount - o.SellerFeeAmount).DefaultIfEmpty().Max();
            return new OpenListingStat(l.Id, l.ReceivableId, l.ClosesAt, mine.Count, mine.Count == 0 ? null : new Money(best, l.FaceValue.Currency).Round());
        }).ToList();
    }

    public async Task<FactorOfferStat> FactorOffersAsync(Guid factorOrgId, CancellationToken cancellationToken)
    {
        var states = await marketplace.Offers.AsNoTracking().Where(o => o.FactorOrgId == factorOrgId).GroupBy(o => o.State)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return new FactorOfferStat(
            states.Where(s => s.Key == OfferState.Active).Sum(s => s.Count),
            states.Where(s => s.Key == OfferState.Accepted).Sum(s => s.Count),
            states.Sum(s => s.Count));
    }

    public async Task<MarketplaceFunnel> FunnelAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var listed = await marketplace.Listings.AsNoTracking().CountAsync(l => l.OpensAt >= since, cancellationToken);
        var withOffers = await marketplace.Listings.AsNoTracking().CountAsync(l => l.OpensAt >= since && marketplace.Offers.Any(o => o.ListingId == l.Id), cancellationToken);
        var accepted = await marketplace.Offers.AsNoTracking().CountAsync(o => o.State == OfferState.Accepted && marketplace.Listings.Any(l => l.Id == o.ListingId && l.OpensAt >= since), cancellationToken);
        return new MarketplaceFunnel(listed, withOffers, accepted);
    }
}
