namespace Modules.Compliance.Contracts;

public enum HoldSubject
{
    Organization,
    Receivable,
    Deal,
}

/// <summary>Whether a hold blocks forward transitions of a subject (ADR-014). Holds themselves come with task 007.</summary>
public interface IHoldQuery
{
    Task<bool> HasActiveHoldAsync(HoldSubject subject, Guid subjectId, CancellationToken cancellationToken);
}

/// <summary>INV-A2: whether a factor organization has a Verified, non-expired FactorAuthorization (task 008).</summary>
public interface IAuthorizationQuery
{
    Task<bool> IsFactorAuthorizedAsync(Guid factorOrgId, CancellationToken cancellationToken);
}
