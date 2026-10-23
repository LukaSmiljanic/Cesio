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

/// <summary>An offer with its listing, as the acceptance needs it (task 014).</summary>
public sealed record AcceptableOffer(
    Guid OfferId,
    Guid ListingId,
    int ListingVersion,
    ListingState ListingState,
    Guid ReceivableId,
    Guid SellerOrgId,
    Guid FactorOrgId,
    OfferState State,
    DateTimeOffset ValidUntil,
    Money FaceValue,
    Money PurchasePrice,
    RecourseType RecourseType,
    int FundingDeadlineBusinessDays,
    string? ConditionsText,
    int FeeScheduleVersion,
    Money PlatformFee,
    Money SellerFee,
    bool VatPending);

/// <summary>The Marketplace side of offer acceptance (task 014), operating on the caller's transaction.</summary>
public interface IListingWriter
{
    Task<AcceptableOffer?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the listing and its offers (UPDLOCK, ROWLOCK), re-checks that the offer is Active and unexpired and the
    /// listing Open, then marks the offer Accepted, the other Active offers Superseded and the listing Closed, with
    /// audit and outbox events.
    /// </summary>
    /// <exception cref="DomainConflictException"><c>offer_not_active</c> or <c>listing_not_open</c>.</exception>
    Task CloseForAcceptanceAsync(Guid offerId, Guid dealId, CancellationToken cancellationToken);
}

/// <summary>Published when a seller accepts an offer; the deal exists from then on.</summary>
public sealed record OfferAccepted(Guid OfferId, Guid ListingId, Guid DealId, Guid FactorOrgId, Guid SellerOrgId) : IIntegrationEvent
{
    public static string EventType => "Marketplace.OfferAccepted";
}

/// <summary>Published for every Active offer that another offer's acceptance or the listing's end superseded.</summary>
public sealed record OfferSuperseded(Guid OfferId, Guid ListingId, Guid FactorOrgId) : IIntegrationEvent
{
    public static string EventType => "Marketplace.OfferSuperseded";
}
