using Shared.Kernel;

namespace Modules.Receivables.Contracts;

/// <summary>Receivable lifecycle (product-spec.md §7.1). Holds are an overlay, not states (ADR-014).</summary>
public enum ReceivableState
{
    Draft,
    PendingVerification,
    Rejected,
    Verified,
    Listed,
    Reserved,
    Assigned,
    Funded,
    Settled,
    RecourseReturned,
    Defaulted,
    Ineligible,
    Cancelled,
}

/// <summary>A receivable as the marketplace shows it (task 012), read regardless of the request's organization.</summary>
public sealed record MarketReceivable(
    Guid Id,
    Guid SellerOrgId,
    ReceivableState State,
    Shared.Kernel.Money FaceValue,
    DateOnly MaturityDate,
    string InvoiceNumber,
    DateOnly IssueDate,
    DateOnly DueDate,
    Shared.Kernel.Money NetAmount,
    Shared.Kernel.Money VatAmount,
    Shared.Kernel.Money GrossAmount,
    string SefStatus,
    DateTimeOffset SefStatusCheckedAt,
    string DebtorPib,
    string? DebtorMb,
    string DebtorLegalName,
    Guid UblDocumentId,
    IReadOnlyList<Guid> EvidenceDocumentIds);

/// <summary>The buyer approval in SEF as far as it is known now (INV-R4).</summary>
public enum SefApproval
{
    Approved,
    NotApproved,
    Unavailable,
}

/// <summary>What the Marketplace module needs from receivables (task 012): reads, the SEF freshness rule and the listing transitions.</summary>
public interface IReceivableMarket
{
    /// <summary>The receivable for any organization; the caller decides who may see it.</summary>
    Task<MarketReceivable?> GetAsync(Guid receivableId, CancellationToken cancellationToken);

    /// <summary>
    /// INV-R4: the stored SEF status if it was checked within <paramref name="maxAge"/>, otherwise a fresh poll (saved
    /// and audited). Unavailable when SEF cannot be asked, so callers fail closed.
    /// </summary>
    Task<SefApproval> EnsureFreshApprovalAsync(Guid receivableId, TimeSpan maxAge, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the receivable from <paramref name="from"/> to <paramref name="to"/> with an audit event, inside the
    /// caller's transaction. False when the receivable is not in <paramref name="from"/>; a concurrent change throws
    /// the EF concurrency exception.
    /// </summary>
    Task<bool> ChangeStateAsync(Guid receivableId, ReceivableState from, ReceivableState to, string reason, CancellationToken cancellationToken);

    /// <summary>The receivable a document belongs to (its invoice UBL or an attached supporting document).</summary>
    Task<Guid?> ReceivableOfDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}

/// <summary>The Receivables side of offer acceptance (task 014), operating on the caller's transaction.</summary>
public interface IReceivableWriter
{
    /// <summary>
    /// Locks the receivable (UPDLOCK, ROWLOCK), requires Listed and INV-R5 (the face value still within the invoice
    /// gross), and moves it to Reserved with an audit event.
    /// </summary>
    /// <exception cref="Shared.Kernel.DomainConflictException"><c>receivable_ineligible</c>.</exception>
    Task ReserveAsync(Guid receivableId, Guid dealId, CancellationToken cancellationToken);

    /// <summary>A deal ended before signing (task 015): the receivable goes back from Reserved to Verified, so the seller may re-list it.</summary>
    Task ReleaseAsync(Guid receivableId, Guid dealId, string reason, CancellationToken cancellationToken);

    /// <summary>The factoring agreement is signed (task 016): Reserved → Assigned, with an audit event (subject to LQ-25).</summary>
    Task AssignAsync(Guid receivableId, Guid dealId, CancellationToken cancellationToken);

    /// <summary>
    /// A later step of the deal (task 017): Assigned → Funded, Funded → Settled/RecourseReturned/Defaulted, or
    /// Assigned → Verified after a termination; locked, checked against <paramref name="from"/> and audited.
    /// </summary>
    /// <exception cref="Shared.Kernel.DomainConflictException"><c>conflict_state</c> when the receivable is not in <paramref name="from"/>.</exception>
    Task MoveForDealAsync(Guid receivableId, Guid dealId, ReceivableState from, ReceivableState to, CancellationToken cancellationToken);
}

/// <summary>A receivable became Ineligible by a job (task 020): maturity too close, or its SEF invoice was withdrawn before assignment.</summary>
public sealed record ReceivableBecameIneligible(Guid ReceivableId, Guid SellerOrgId, ReceivableState PreviousState, string Reason) : IIntegrationEvent
{
    public static string EventType => "Receivables.ReceivableBecameIneligible";
}

/// <summary>SEF shows the invoice cancelled, reversed, deleted or rejected (task 020); <paramref name="State"/> is the receivable's state then.</summary>
public sealed record ReceivableSefStatusChanged(Guid ReceivableId, Guid SellerOrgId, string SefStatus, ReceivableState State) : IIntegrationEvent
{
    public static string EventType => "Receivables.ReceivableSefStatusChanged";
}

/// <summary>Read-only figures for the dashboards (task 022); computed from the rows, never stored.</summary>
public interface IReceivableStats
{
    /// <summary>Receivables per state, of one seller or (null) of the platform.</summary>
    Task<IReadOnlyDictionary<ReceivableState, int>> CountByStateAsync(Guid? sellerOrgId, CancellationToken cancellationToken);

    /// <summary>Receivables waiting for a manual review.</summary>
    Task<int> ReviewQueueAsync(CancellationToken cancellationToken);

    /// <summary>Receivables imported since <paramref name="since"/> (the start of the funnel).</summary>
    Task<int> ImportedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
