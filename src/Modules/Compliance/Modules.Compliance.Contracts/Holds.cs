namespace Modules.Compliance.Contracts;

public enum HoldSubject
{
    Organization,
    Receivable,
    Deal,
}

/// <summary>Hold kinds (domain-model.md); lifting a FraudInvestigation hold needs four-eyes (task 007).</summary>
public enum HoldKind
{
    ComplianceHold,
    Disputed,
    FraudInvestigation,
}

/// <summary>
/// The hold policy (task 021, ADR-014; documented in domain-model.md): in the MVP every kind blocks the same forward
/// transitions; they differ in who may lift them (FraudInvestigation: two officers) and why they were placed.
/// </summary>
public static class HoldPolicy
{
    /// <summary>What an active hold on each subject blocks.</summary>
    public static readonly IReadOnlyDictionary<HoldSubject, string> Blocks = new Dictionary<HoldSubject, string>
    {
        [HoldSubject.Organization] = "listing, offers and acceptance by the organization, every deal step of its deals, being seen as a factor",
        [HoldSubject.Receivable] = "submission and verification decisions, listing, acceptance, every deal step of its deal",
        [HoldSubject.Deal] = "every deal step (CDD, contract, terms, signatures, funding report and confirmation, outcome), the KYB package",
    };

    /// <summary>Kinds that need a second, different officer to lift.</summary>
    public static readonly IReadOnlyList<HoldKind> FourEyesLift = [HoldKind.FraudInvestigation];
}

/// <summary>Whether a hold blocks forward transitions of a subject (ADR-014).</summary>
public interface IHoldQuery
{
    Task<bool> HasActiveHoldAsync(HoldSubject subject, Guid subjectId, CancellationToken cancellationToken);
}

/// <summary>Holds placed by the system (task 020): a storno after assignment, a factor whose authorization ended. Runs in the caller's transaction.</summary>
public interface IHoldWriter
{
    /// <summary>Places a hold unless the subject already has an active one of that kind; audited with the reason.</summary>
    Task PlaceSystemHoldAsync(HoldSubject subject, Guid subjectId, HoldKind kind, string reason, Guid? organizationId, CancellationToken cancellationToken);

    /// <summary>Lifts the subject's active holds of <paramref name="kind"/> (not FraudInvestigation, which needs four-eyes); audited.</summary>
    Task LiftSystemHoldAsync(HoldSubject subject, Guid subjectId, HoldKind kind, string reason, Guid? organizationId, CancellationToken cancellationToken);
}

/// <summary>INV-A2: whether a factor organization has a Verified, non-expired FactorAuthorization (task 008).</summary>
public interface IAuthorizationQuery
{
    Task<bool> IsFactorAuthorizedAsync(Guid factorOrgId, CancellationToken cancellationToken);
}

public enum ScreeningOutcome
{
    Clear,
    PotentialMatch,

    /// <summary>No list has been imported yet, so nothing can be said (callers ask a person to look).</summary>
    NoLists,
}

/// <summary>A list entry a screened name may match.</summary>
public sealed record ScreeningHit(string ListSource, string EntryName, decimal Score);

public sealed record ScreeningAnswer(ScreeningOutcome Outcome, IReadOnlyList<ScreeningHit> Hits);

/// <summary>Sanctions screening against the locally imported lists (task 007; also used for debtors in task 011).</summary>
public interface IScreeningService
{
    Task<ScreeningAnswer> ScreenAsync(string name, CancellationToken cancellationToken);
}

/// <summary>A beneficial owner as the KYB package shares it (task 015).</summary>
public sealed record BeneficialOwnerSummary(string Name, DateOnly? DateOfBirth, string? Nationality, decimal? OwnershipPercent, string? ControlType, bool PepDeclared);

/// <summary>What other modules may know about an organization's KYB (task 007).</summary>
public interface IKybQuery
{
    /// <summary>Whether the organization consented to sharing its KYB package with the factor of a deal.</summary>
    Task<bool> HasSharingConsentAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BeneficialOwnerSummary>> BeneficialOwnersAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The latest decided risk rating and screening outcome, for the KYB package summary (not the raw hits).</summary>
    Task<(string? RiskRating, string ScreeningSummary)> SummaryAsync(Guid organizationId, CancellationToken cancellationToken);
}
