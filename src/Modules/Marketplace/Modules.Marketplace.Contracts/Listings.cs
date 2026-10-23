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

/// <summary>Offer lifecycle (product-spec.md §7.3); offers are immutable except for their state (ADR-013).</summary>
public enum OfferState
{
    Active,
    Accepted,
    Withdrawn,
    Rejected,
    Expired,
    Superseded,
}

public enum RecourseType
{
    Recourse,
    NonRecourse,
}

/// <summary>Published when a factor submits an offer; Notifications tells the seller (task 019).</summary>
public sealed record OfferSubmitted(Guid OfferId, Guid ListingId, Guid FactorOrgId, Guid SellerOrgId) : IIntegrationEvent
{
    public static string EventType => "Marketplace.OfferSubmitted";
}
