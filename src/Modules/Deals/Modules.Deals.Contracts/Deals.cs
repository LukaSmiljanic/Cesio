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

/// <summary>Published when both parties validly signed (task 016); the receivable is Assigned.</summary>
public sealed record DealSigned(Guid DealId, Guid ReceivableId, Guid SellerOrgId, Guid FactorOrgId) : IIntegrationEvent
{
    public static string EventType => "Deals.DealSigned";
}

/// <summary>The factor's report on a funded deal (api.md §3.10).</summary>
public enum OutcomeType
{
    Settled,
    RecourseExercised,
    Defaulted,
}

/// <summary>Published when the factor reports the payment to the seller (task 017).</summary>
public sealed record DealFundingReported(Guid DealId, Guid SellerOrgId, Guid FactorOrgId, bool AmountMatches) : IIntegrationEvent
{
    public static string EventType => "Deals.DealFundingReported";
}

/// <summary>Published when the seller confirms receipt; task 018 creates the fee charge from it (INV-F3).</summary>
/// <param name="Reference">The deal reference, with the invoice number and face value, snapshotted for the fee statement (task 018).</param>
public sealed record DealFunded(
    Guid DealId,
    Guid SellerOrgId,
    Guid FactorOrgId,
    int FeeScheduleVersion,
    Money PlatformFee,
    Money SellerFee,
    string Reference,
    string InvoiceNumber,
    Money FaceValue,
    DateTimeOffset FundedAt) : IIntegrationEvent
{
    public static string EventType => "Deals.DealFunded";
}

/// <summary>Published when the factor records the outcome of a funded deal.</summary>
public sealed record DealClosed(Guid DealId, OutcomeType Outcome) : IIntegrationEvent
{
    public static string EventType => "Deals.DealClosed";
}

/// <summary>Published when staff terminate a signed deal before funding (INV-R6).</summary>
public sealed record DealTerminated(Guid DealId, Guid ReceivableId, string Reason) : IIntegrationEvent
{
    public static string EventType => "Deals.DealTerminated";
}

/// <summary>Read access to deals for other modules (task 019).</summary>
public interface IDealQuery
{
    /// <summary>The factors that have a deal with the seller that is not yet closed, cancelled or terminated.</summary>
    Task<IReadOnlyList<Guid>> FactorsWithOpenDealsAsync(Guid sellerOrgId, CancellationToken cancellationToken);
}

public sealed record MaturityBucket(string Bucket, int Count, IReadOnlyList<Shared.Kernel.Money> FaceValue);

/// <summary>Discount ÷ face value over the funded deals of one currency (indicative cost; never across currencies).</summary>
public sealed record CurrencyRate(string Currency, decimal Rate);

/// <param name="AverageDiscountRate">One rate per currency of the funded deals; empty when none.</param>
public sealed record OrganizationDealStats(
    int Open, IReadOnlyList<Shared.Kernel.Money> FundedThisMonth, IReadOnlyList<Shared.Kernel.Money> FundedThisYear,
    IReadOnlyList<CurrencyRate> AverageDiscountRate, int OverdueOutcomes, IReadOnlyList<MaturityBucket> Portfolio);

public sealed record StuckDeals(DealState State, int Count);

/// <param name="Gmv">Face values of the deals funded this month (financial-model.md §3); <paramref name="PlatformFees"/> their platform fees (take rate = fees ÷ GMV).</param>
public sealed record PlatformDealStats(IReadOnlyList<StuckDeals> Stuck, int FundedThisMonth, IReadOnlyList<Shared.Kernel.Money> Gmv, IReadOnlyList<Shared.Kernel.Money> PlatformFees);

/// <summary>Read-only figures for the dashboards (task 022), computed from the deal rows.</summary>
public interface IDealStats
{
    Task<OrganizationDealStats> ForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Deals that have stayed in a step longer than <paramref name="stuckDays"/> days, and this month's funded volume.</summary>
    Task<PlatformDealStats> PlatformAsync(int stuckDays, CancellationToken cancellationToken);
}
