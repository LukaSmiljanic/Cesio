using Shared.Kernel;

namespace Modules.Notifications.Contracts;

/// <summary>Notification types of product-spec §8 (task 019). Mandatory types cannot be turned off.</summary>
public static class NotificationTypes
{
    public const string DealActionRequired = "deal_action_required";

    public const string FundingReported = "funding_reported";

    public const string FundingConfirmed = "funding_confirmed";

    public const string DealCancelled = "deal_cancelled";

    public const string OfferReceived = "offer_received";

    public const string OfferWithdrawn = "offer_withdrawn";

    public const string OfferAccepted = "offer_accepted";

    public const string OfferSuperseded = "offer_superseded";

    public const string NewListing = "new_listing";

    public const string ReceivableVerification = "receivable_verification";

    public const string KybDecision = "kyb_decision";

    public const string FactorAuthorization = "factor_authorization";

    public const string Hold = "hold";

    public const string SefStatusChanged = "sef_status_changed";

    public const string FeeStatement = "fee_statement";

    /// <summary>Security events (security.md §2): sent to the Owners and the official email.</summary>
    public const string Security = "security";

    /// <summary>Reminders from the scheduled jobs (task 020): signature deadline, outcome overdue, authorization re-check.</summary>
    public const string Reminder = "reminder";

    public static readonly string[] All =
    [
        DealActionRequired, FundingReported, FundingConfirmed, DealCancelled, OfferReceived, OfferWithdrawn, OfferAccepted, OfferSuperseded,
        NewListing, ReceivableVerification, KybDecision, FactorAuthorization, Hold, SefStatusChanged, FeeStatement, Security, Reminder,
    ];

    /// <summary>Security, deal steps, funding and holds (task 019 requirement 2).</summary>
    public static readonly string[] Mandatory = [DealActionRequired, FundingReported, FundingConfirmed, DealCancelled, OfferAccepted, Hold, Security, KybDecision, FactorAuthorization];
}

/// <summary>
/// A module asks Notifications to tell an organization's members something (task 019), for events that have no domain
/// event of their own. <paramref name="Link"/> is an app path such as <c>/deals/{id}</c>; <paramref name="Reference"/>
/// is a non-confidential reference such as a deal number (never amounts or debtor names). A job that may find the same
/// reminder again passes <paramref name="DedupeKey"/> (e.g. "signature-deadline:{dealId}"), so it is sent once.
/// </summary>
public sealed record OrganizationNotificationRequested(Guid OrganizationId, string Type, string Link, string? Reference, string? DedupeKey = null) : IIntegrationEvent
{
    public static string EventType => "Notifications.OrganizationNotificationRequested";

    /// <summary>The idempotency id of a deduplicated reminder.</summary>
    public static Guid KeyId(string dedupeKey) => new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dedupeKey))[..16]);
}
