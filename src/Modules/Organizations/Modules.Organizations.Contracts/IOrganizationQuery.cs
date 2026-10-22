using Shared.Kernel;

namespace Modules.Organizations.Contracts;

public enum OrganizationType
{
    Seller,
    Factor,
}

/// <summary>domain-model.md §3 onboardingStatus.</summary>
public enum OnboardingStatus
{
    Draft,
    Submitted,
    UnderReview,
    Approved,
    Rejected,
    Suspended,
}

public sealed record OrganizationSummary(Guid Id, OrganizationType Type, OnboardingStatus Status, string Pib, string? LegalName)
{
    /// <summary>Only Approved organizations can transact (task 005 business rule; INV-A1 adds the hold check).</summary>
    public bool IsApproved => Status == OnboardingStatus.Approved;
}

public sealed record MembershipSummary(Guid OrganizationId, Guid UserId, bool IsSignatory);

/// <summary>Read access to organizations for other modules (task 005 technical requirements).</summary>
public interface IOrganizationQuery
{
    Task<OrganizationSummary?> GetAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The user's active membership in the organization, or null.</summary>
    Task<MembershipSummary?> GetMembershipAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task<bool> IsSignatoryAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
}

/// <summary>Published through the outbox when an organization is submitted for review (KYB starts in task 007).</summary>
public sealed record OrganizationSubmitted(Guid OrganizationId, OrganizationType Type) : IIntegrationEvent
{
    public static string EventType => "Organizations.OrganizationSubmitted";
}
