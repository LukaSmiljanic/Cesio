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

    /// <summary>
    /// The names of the organization's legal representatives valid on <paramref name="on"/>, the persons whose QES
    /// may bind it (task 016; verified attorneys come with task 007, LQ-61).
    /// </summary>
    Task<IReadOnlyList<string>> AuthorizedSignerNamesAsync(Guid organizationId, DateOnly on, CancellationToken cancellationToken);
}

/// <summary>Published through the outbox when an organization is submitted for review (KYB starts in task 007).</summary>
public sealed record OrganizationSubmitted(Guid OrganizationId, OrganizationType Type) : IIntegrationEvent
{
    public static string EventType => "Organizations.OrganizationSubmitted";
}

public enum SefConnectionStatus
{
    Connected,
    Invalid,
}

/// <param name="SecretName">Name of the API key in the secret store; the key itself never leaves it.</param>
public sealed record SefConnectionInfo(Guid OrganizationId, SefConnectionStatus Status, string SecretName, string? SupplierPib);

/// <summary>The organization's SEF connection for modules that call SEF on its behalf (Receivables).</summary>
public interface ISefConnections
{
    Task<SefConnectionInfo?> GetAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>SEF answered 401 for the stored key: the connection becomes Invalid (integrations.md §1).</summary>
    Task MarkInvalidAsync(Guid organizationId, CancellationToken cancellationToken);
}

/// <summary>A valid SEF key proved that the organization issues invoices under this PIB; task 007a claims the PIB from it (ADR-020).</summary>
public sealed record SefConnectionValidated(Guid OrganizationId, string SupplierPib) : IIntegrationEvent
{
    public static string EventType => "Organizations.SefConnectionValidated";
}

/// <summary>The stored SEF key stopped working; Notifications tells the Owners (task 019).</summary>
public sealed record SefConnectionInvalidated(Guid OrganizationId) : IIntegrationEvent
{
    public static string EventType => "Organizations.SefConnectionInvalidated";
}
