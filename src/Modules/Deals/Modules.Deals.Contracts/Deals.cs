using Shared.Kernel;

namespace Modules.Deals.Contracts;

/// <summary>Deal lifecycle (product-spec.md §7.4).</summary>
public enum DealState
{
    AwaitingFactorCdd,
    AwaitingContract,
    AwaitingSignatures,
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The state name of product-spec.md §7.4.")]
    Signed,
    FundingReported,
    Funded,
    Closed,
    Cancelled,
    Terminated,
}

/// <summary>Published when an accepted offer creates a deal (task 014).</summary>
public sealed record DealCreated(Guid DealId, string Reference, Guid ReceivableId, Guid SellerOrgId, Guid FactorOrgId) : IIntegrationEvent
{
    public static string EventType => "Deals.DealCreated";
}

/// <summary>Published when a deal ends before signing (task 015); the receivable is Verified again.</summary>
public sealed record DealCancelled(Guid DealId, string Reason, Guid SellerOrgId, Guid FactorOrgId) : IIntegrationEvent
{
    public static string EventType => "Deals.DealCancelled";
}
