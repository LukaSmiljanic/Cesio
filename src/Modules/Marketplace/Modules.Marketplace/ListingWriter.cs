using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Marketplace.Persistence;
using Shared.Infrastructure.Outbox;
using Shared.Kernel;

namespace Modules.Marketplace;

/// <summary><see cref="IListingWriter"/>: the listing and offer transitions of an acceptance (task 014).</summary>
internal sealed class ListingWriter(MarketplaceDbContext marketplace, IAuditWriter audit, IOutbox outbox, TimeProvider timeProvider) : IListingWriter
{
    public async Task<AcceptableOffer?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken)
    {
        if (await marketplace.Offers.AsNoTracking().SingleOrDefaultAsync(o => o.Id == offerId, cancellationToken) is not { } offer)
        {
            return null;
        }

        var listing = await marketplace.Listings.AsNoTracking().SingleAsync(l => l.Id == offer.ListingId, cancellationToken);
        var currency = offer.PurchasePrice.Currency;
        return new AcceptableOffer(
            offer.Id,
            listing.Id,
            listing.Version,
            listing.State,
            listing.ReceivableId,
            listing.SellerOrgId,
            offer.FactorOrgId,
            offer.State,
            offer.ValidUntil,
            listing.FaceValue,
            offer.PurchasePrice,
            offer.RecourseType,
            offer.FundingDeadlineBusinessDays,
            offer.ConditionsText,
            offer.FeeScheduleVersion,
            new Money(offer.PlatformFeeAmount, currency),
            new Money(offer.SellerFeeAmount, currency),
            offer.VatPending);
    }

    public async Task CloseForAcceptanceAsync(Guid offerId, Guid dealId, CancellationToken cancellationToken)
    {
        var listingId = await marketplace.Offers.AsNoTracking().Where(o => o.Id == offerId).Select(o => o.ListingId).SingleAsync(cancellationToken);
        // Pessimistic locks at the hottest point (database.md §4): a second acceptance waits here, then sees Closed.
        await marketplace.Database.ExecuteSqlAsync($"SELECT id FROM marketplace.listings WITH (UPDLOCK, ROWLOCK) WHERE id = {listingId}", cancellationToken);
        await marketplace.Database.ExecuteSqlAsync($"SELECT id FROM marketplace.offers WITH (UPDLOCK, ROWLOCK) WHERE listing_id = {listingId}", cancellationToken);
        var listing = await marketplace.Listings.SingleAsync(l => l.Id == listingId, cancellationToken);
        var listingOffers = await marketplace.Offers.Where(o => o.ListingId == listingId).ToListAsync(cancellationToken);
        var offer = listingOffers.Single(o => o.Id == offerId);
        if (listing.State != ListingState.Open)
        {
            throw new DomainConflictException("listing_not_open", "The listing is no longer open.");
        }

        if (offer.State != OfferState.Active || offer.ValidUntil <= timeProvider.GetUtcNow())
        {
            throw new DomainConflictException("offer_not_active", "The offer is no longer active.");
        }

        offer.State = OfferState.Accepted;
        listing.State = ListingState.Closed;
        var superseded = listingOffers.Where(o => o.Id != offerId && o.State == OfferState.Active).ToList();
        foreach (var other in superseded)
        {
            other.State = OfferState.Superseded;
        }

        await marketplace.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("Offer.Accepted", "Offer", offer.Id, listing.SellerOrgId,
            PreviousState: nameof(OfferState.Active), NewState: nameof(OfferState.Accepted), Related: [new AuditReference("Deal", dealId)]), cancellationToken);
        foreach (var other in superseded)
        {
            await audit.WriteAsync(new AuditEntry("Offer.Superseded", "Offer", other.Id, other.FactorOrgId,
                PreviousState: nameof(OfferState.Active), NewState: nameof(OfferState.Superseded), Related: [new AuditReference("Listing", listingId)]), cancellationToken);
            await outbox.AddAsync(new OfferSuperseded(other.Id, listingId, other.FactorOrgId), cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("Listing.Closed", "Listing", listingId, listing.SellerOrgId,
            PreviousState: nameof(ListingState.Open), NewState: nameof(ListingState.Closed), Related: [new AuditReference("Deal", dealId)]), cancellationToken);
        await outbox.AddAsync(new OfferAccepted(offer.Id, listingId, dealId, offer.FactorOrgId, listing.SellerOrgId), cancellationToken);
    }
}
