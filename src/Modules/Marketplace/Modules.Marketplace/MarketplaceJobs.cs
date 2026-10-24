using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Marketplace.Persistence;
using Modules.Notifications.Contracts;
using Modules.Receivables.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;

namespace Modules.Marketplace;

/// <summary>
/// Time-based and event-driven marketplace transitions (task 020): listing and offer expiry, listings of receivables
/// that became Ineligible, and INV-A2 for factors whose authorization ended. Per-item transactions, audited as System.
/// </summary>
internal sealed class MarketplaceJobs(
    MarketplaceDbContext marketplace,
    OfferService offers,
    IReceivableMarket receivables,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    TimeProvider timeProvider) :
    IIntegrationEventConsumer<ReceivableBecameIneligible>,
    IIntegrationEventConsumer<FactorAuthorizationRevoked>,
    IIntegrationEventConsumer<FactorAuthorizationExpired>
{
    public const string IneligibleConsumerName = "marketplace.receivable-ineligible";

    public const string RevokedConsumerName = "marketplace.factor-authorization-revoked";

    public const string ExpiredConsumerName = "marketplace.factor-authorization-expired";

    private const int BatchSize = 100;

    /// <summary>Every 5 minutes: Open listings past <c>closesAt</c> → Expired, the receivable back to Verified, Active offers Superseded.</summary>
    public async Task<int> ExpireListingsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await marketplace.Listings.Where(l => l.State == ListingState.Open && l.ClosesAt <= now).OrderBy(l => l.ClosesAt).Take(BatchSize).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var listing in due)
        {
            count += await TryAsync(ct => EndListingAsync(listing, ListingState.Expired, "listing_expired", releaseReceivable: true, ct), cancellationToken) ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// Every 5 minutes: Active offers past <c>validUntil</c> → Expired; an offer that expires within 24 hours gets one
    /// reminder to its factor (product-spec §8).
    /// </summary>
    public async Task<int> ExpireOffersAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await marketplace.Offers.Where(o => o.State == OfferState.Active && o.ValidUntil <= now).OrderBy(o => o.ValidUntil).Take(BatchSize).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var offer in due)
        {
            count += await TryAsync(ct => ExpireOfferAsync(offer, "offer_expired", ct), cancellationToken) ? 1 : 0;
        }

        var soon = await marketplace.Offers.AsNoTracking()
            .Where(o => o.State == OfferState.Active && o.ValidUntil > now && o.ValidUntil <= now.AddHours(24))
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var offer in soon)
        {
            await unitOfWork.ExecuteInTransactionAsync(
                ct => outbox.AddAsync(new OrganizationNotificationRequested(offer.FactorOrgId, NotificationTypes.Reminder, "/offers", null, $"offer-expiring:{offer.Id}"), ct),
                cancellationToken);
        }

        return count;
    }

    /// <summary>The receivable became Ineligible: its Open or Suspended listing is withdrawn by the system and its offers superseded.</summary>
    public async Task HandleAsync(ReceivableBecameIneligible integrationEvent, Guid eventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (await marketplace.Listings.SingleOrDefaultAsync(
                l => l.ReceivableId == integrationEvent.ReceivableId && (l.State == ListingState.Open || l.State == ListingState.Suspended), cancellationToken) is { } listing)
        {
            await EndListingAsync(listing, ListingState.Withdrawn, $"receivable_ineligible:{integrationEvent.Reason}", releaseReceivable: false, cancellationToken);
        }
    }

    public Task HandleAsync(FactorAuthorizationRevoked integrationEvent, Guid eventId, CancellationToken cancellationToken) =>
        ExpireFactorOffersAsync(integrationEvent?.FactorOrgId ?? throw new ArgumentNullException(nameof(integrationEvent)), "factor_authorization_revoked", cancellationToken);

    public Task HandleAsync(FactorAuthorizationExpired integrationEvent, Guid eventId, CancellationToken cancellationToken) =>
        ExpireFactorOffersAsync(integrationEvent?.FactorOrgId ?? throw new ArgumentNullException(nameof(integrationEvent)), "factor_authorization_expired", cancellationToken);

    /// <summary>INV-A2: the factor's Active offers expire (its deals before Signed get a hold in Deals).</summary>
    private async Task ExpireFactorOffersAsync(Guid factorOrgId, string reason, CancellationToken cancellationToken)
    {
        foreach (var offer in await marketplace.Offers.Where(o => o.FactorOrgId == factorOrgId && o.State == OfferState.Active).ToListAsync(cancellationToken))
        {
            await ExpireOfferAsync(offer, reason, cancellationToken);
        }
    }

    private async Task ExpireOfferAsync(Offer offer, string reason, CancellationToken cancellationToken)
    {
        offer.State = OfferState.Expired;
        await marketplace.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("Offer.Expired", "Offer", offer.Id, offer.FactorOrgId,
            PreviousState: nameof(OfferState.Active), NewState: nameof(OfferState.Expired), Reason: reason, Related: [new AuditReference("Listing", offer.ListingId)]), cancellationToken);
        await outbox.AddAsync(new OfferSuperseded(offer.Id, offer.ListingId, offer.FactorOrgId), cancellationToken);
    }

    private async Task EndListingAsync(Listing listing, ListingState to, string reason, bool releaseReceivable, CancellationToken cancellationToken)
    {
        var previous = listing.State;
        listing.State = to;
        await marketplace.SaveChangesAsync(cancellationToken);
        if (releaseReceivable)
        {
            await receivables.ChangeStateAsync(listing.ReceivableId, ReceivableState.Listed, ReceivableState.Verified, reason, cancellationToken);
        }

        await offers.SupersedeActiveAsync(listing.Id, cancellationToken);
        await audit.WriteAsync(new AuditEntry($"Listing.{to}", "Listing", listing.Id, listing.SellerOrgId,
            PreviousState: previous.ToString(), NewState: to.ToString(), Reason: reason), cancellationToken);
        if (to == ListingState.Withdrawn)
        {
            await outbox.AddAsync(new ListingWithdrawn(listing.Id, listing.ReceivableId, listing.SellerOrgId), cancellationToken);
        }
    }

    private async Task<bool> TryAsync(Func<CancellationToken, Task> item, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(item, cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Changed by a user at the same moment (e.g. the offer accepted): the next run looks again.
            marketplace.ChangeTracker.Clear();
            return false;
        }
    }
}
