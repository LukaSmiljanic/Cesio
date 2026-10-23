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
