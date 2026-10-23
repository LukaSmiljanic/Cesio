using Shared.Kernel;

namespace Modules.Marketplace.Contracts;

/// <summary>Listing lifecycle (product-spec.md §7.2).</summary>
public enum ListingState
{
    Open,
    Closed,
    Withdrawn,
    Expired,
    Suspended,
}

public enum AudienceType
{
    AllVerifiedFactors,
    Selected,
}

/// <summary>Published through the outbox when a seller opens a listing (task 012).</summary>
public sealed record ListingOpened(Guid ListingId, Guid ReceivableId, Guid SellerOrgId) : IIntegrationEvent
{
    public static string EventType => "Marketplace.ListingOpened";
}

/// <summary>Published when the seller withdraws a listing; task 013 supersedes the listing's Active offers on it.</summary>
public sealed record ListingWithdrawn(Guid ListingId, Guid ReceivableId, Guid SellerOrgId) : IIntegrationEvent
{
    public static string EventType => "Marketplace.ListingWithdrawn";
}
