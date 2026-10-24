using Modules.Compliance.Contracts;
using Modules.Deals.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Notifications.Contracts;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Outbox;

namespace Modules.Notifications;

/// <summary>
/// Maps domain events to notifications (task 019, product-spec §8). Emails carry no amounts or debtor names, only a
/// type, a deal reference where there is one, and a link into the app.
/// </summary>
internal sealed class NotificationConsumers(NotificationService notifications, IListingMatches listings, IDealQuery deals) :
    IIntegrationEventConsumer<DealCreated>,
    IIntegrationEventConsumer<DealSigned>,
    IIntegrationEventConsumer<DealFundingReported>,
    IIntegrationEventConsumer<DealFunded>,
    IIntegrationEventConsumer<DealCancelled>,
    IIntegrationEventConsumer<OfferSubmitted>,
    IIntegrationEventConsumer<OfferAccepted>,
    IIntegrationEventConsumer<OfferSuperseded>,
    IIntegrationEventConsumer<ListingOpened>,
    IIntegrationEventConsumer<OrganizationApproved>,
    IIntegrationEventConsumer<BankAccountAdded>,
    IIntegrationEventConsumer<BankAccountVerified>,
    IIntegrationEventConsumer<BankAccountDeactivated>,
    IIntegrationEventConsumer<SefConnectionInvalidated>,
    IIntegrationEventConsumer<FactorAuthorizationRevoked>,
    IIntegrationEventConsumer<FactorAuthorizationExpired>,
    IIntegrationEventConsumer<OrganizationNotificationRequested>
{
    /// <summary>The consumer names, one per event type (the outbox records deliveries per consumer).</summary>
    public static readonly Func<string, string> Name = eventType => $"notifications.{eventType}";

    public async Task HandleAsync(DealCreated e, Guid eventId, CancellationToken ct)
    {
        await Deal(eventId, e.SellerOrgId, e.DealId, e.Reference, NotificationTypes.DealActionRequired, ct);
        await Deal(eventId, e.FactorOrgId, e.DealId, e.Reference, NotificationTypes.DealActionRequired, ct);
    }

    public async Task HandleAsync(DealSigned e, Guid eventId, CancellationToken ct)
    {
        await Deal(eventId, e.SellerOrgId, e.DealId, null, NotificationTypes.DealActionRequired, ct);
        await Deal(eventId, e.FactorOrgId, e.DealId, null, NotificationTypes.DealActionRequired, ct);
    }

    public Task HandleAsync(DealFundingReported e, Guid eventId, CancellationToken ct) =>
        Deal(eventId, e.SellerOrgId, e.DealId, null, NotificationTypes.FundingReported, ct);

    public async Task HandleAsync(DealFunded e, Guid eventId, CancellationToken ct)
    {
        await Deal(eventId, e.SellerOrgId, e.DealId, null, NotificationTypes.FundingConfirmed, ct);
        await Deal(eventId, e.FactorOrgId, e.DealId, null, NotificationTypes.FundingConfirmed, ct);
    }

    public async Task HandleAsync(DealCancelled e, Guid eventId, CancellationToken ct)
    {
        await Deal(eventId, e.SellerOrgId, e.DealId, null, NotificationTypes.DealCancelled, ct);
        await Deal(eventId, e.FactorOrgId, e.DealId, null, NotificationTypes.DealCancelled, ct);
    }

    public Task HandleAsync(OfferSubmitted e, Guid eventId, CancellationToken ct) =>
        notifications.NotifyOrganizationAsync(eventId, e.SellerOrgId, NotificationTypes.OfferReceived, $"/listings/{e.ListingId}/offers", null, ct);

    public Task HandleAsync(OfferAccepted e, Guid eventId, CancellationToken ct) =>
        Deal(eventId, e.FactorOrgId, e.DealId, null, NotificationTypes.OfferAccepted, ct);

    public Task HandleAsync(OfferSuperseded e, Guid eventId, CancellationToken ct) =>
        notifications.NotifyOrganizationAsync(eventId, e.FactorOrgId, NotificationTypes.OfferSuperseded, "/offers", null, ct);

    /// <summary>Factors whose appetite matches hear about the listing in the hourly digest.</summary>
    public async Task HandleAsync(ListingOpened e, Guid eventId, CancellationToken ct)
    {
        foreach (var factor in await listings.FactorsMatchingAsync(e.ListingId, ct))
        {
            await notifications.NotifyOrganizationAsync(eventId, factor, NotificationTypes.NewListing, $"/marketplace/{e.ListingId}", null, ct);
        }
    }

    public Task HandleAsync(OrganizationApproved e, Guid eventId, CancellationToken ct) =>
        notifications.NotifyOrganizationAsync(eventId, e.OrganizationId, NotificationTypes.KybDecision, $"/organizations/{e.OrganizationId}", null, ct);

    public Task HandleAsync(BankAccountAdded e, Guid eventId, CancellationToken ct) => Account(eventId, e.OrganizationId, ct);

    public Task HandleAsync(BankAccountVerified e, Guid eventId, CancellationToken ct) => Account(eventId, e.OrganizationId, ct);

    public Task HandleAsync(BankAccountDeactivated e, Guid eventId, CancellationToken ct) => Account(eventId, e.OrganizationId, ct);

    public Task HandleAsync(SefConnectionInvalidated e, Guid eventId, CancellationToken ct) =>
        notifications.NotifyOrganizationAsync(eventId, e.OrganizationId, NotificationTypes.Security, $"/organizations/{e.OrganizationId}", null, ct);

    public Task HandleAsync(FactorAuthorizationRevoked e, Guid eventId, CancellationToken ct) =>
        notifications.NotifyOrganizationAsync(eventId, e.FactorOrgId, NotificationTypes.FactorAuthorization, $"/organizations/{e.FactorOrgId}", null, ct);

    public Task HandleAsync(FactorAuthorizationExpired e, Guid eventId, CancellationToken ct) =>
        notifications.NotifyOrganizationAsync(eventId, e.FactorOrgId, NotificationTypes.FactorAuthorization, $"/organizations/{e.FactorOrgId}", null, ct);

    public Task HandleAsync(OrganizationNotificationRequested e, Guid eventId, CancellationToken ct) =>
        NotificationTypes.All.Contains(e.Type)
            ? notifications.NotifyOrganizationAsync(e.DedupeKey is { } key ? OrganizationNotificationRequested.KeyId(key) : eventId, e.OrganizationId, e.Type, e.Link, e.Reference, ct)
            : throw new InvalidOperationException($"Unknown notification type {e.Type}.");

    private Task Deal(Guid eventId, Guid orgId, Guid dealId, string? reference, string type, CancellationToken ct) =>
        notifications.NotifyOrganizationAsync(eventId, orgId, type, $"/deals/{dealId}", reference, ct);

    /// <summary>Payout account changes go to the security recipients and to the factors of the seller's open deals (security.md §2).</summary>
    private async Task Account(Guid eventId, Guid orgId, CancellationToken ct)
    {
        await notifications.NotifyOrganizationAsync(eventId, orgId, NotificationTypes.Security, $"/organizations/{orgId}", null, ct);
        foreach (var factor in await deals.FactorsWithOpenDealsAsync(orgId, ct))
        {
            await notifications.NotifyOrganizationAsync(eventId, factor, NotificationTypes.Security, "/deals", null, ct);
        }
    }
}
