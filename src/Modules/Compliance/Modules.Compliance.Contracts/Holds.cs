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
