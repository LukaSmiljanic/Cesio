using Modules.Organizations.Contracts;

namespace Modules.Organizations.Persistence;

/// <summary>domain-model.md §3 Organization. A seller starts as a private Draft claim with only its PIB (ADR-020).</summary>
internal sealed class Organization
{
    public Guid Id { get; init; }

    public OrganizationType OrgType { get; init; }

    public string? LegalName { get; set; }

    public required string Pib { get; init; }

    public string? Mb { get; set; }

    public string? LegalForm { get; set; }

    public string? RegisteredAddress { get; set; }

    /// <summary>Set by a valid SEF key for sellers (task 007a) or at invitation for factors.</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>The company's email in APR, recorded by compliance (task 007a); confirmations of sensitive changes go there.</summary>
    public string? OfficialEmail { get; set; }

    /// <summary>The APR snapshot the official email was taken from.</summary>
    public Guid? OfficialEmailSourceDocumentId { get; set; }

    public Guid? OfficialEmailRecordedBy { get; set; }

    public DateTimeOffset? OfficialEmailRecordedAt { get; set; }

    public OnboardingStatus OnboardingStatus { get; set; }

    public string? RiskRating { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public int Version { get; set; }
}

internal enum MembershipStatus
{
    Active,
    Removed,
}

/// <summary>Every member is Owner; Signatory is a flag (ADR-020).</summary>
internal sealed class Membership
{
    public const string OwnerRole = "Owner";

    public Guid Id { get; init; }

    public Guid OrgId { get; init; }

    public Guid UserId { get; init; }

    public string Role { get; init; } = OwnerRole;

    public bool IsSignatory { get; set; }

    public MembershipStatus Status { get; set; }

    /// <summary>The APR legal representative this member is (one of the two ways to qualify as Signatory).</summary>
    public Guid? LegalRepresentativeId { get; set; }

    /// <summary>A power-of-attorney document (documents arrive in task 006; staff verify it in task 007).</summary>
    public Guid? PowerOfAttorneyDocumentId { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    public int Version { get; set; }
}

internal sealed class LegalRepresentative
{
    public Guid Id { get; init; }

    public Guid OrgId { get; init; }

    public required string Name { get; set; }

    /// <summary>The <i>zastupnik</i> role as written in APR, e.g. "Direktor".</summary>
    public required string Role { get; set; }

    /// <summary><c>Document</c> (entered by the organization) or <c>AprSnapshot</c> (task 007).</summary>
    public required string Source { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public Guid? LinkedUserId { get; set; }

    public int Version { get; set; }
}

internal sealed class TermsAcceptance
{
    public Guid Id { get; init; }

    public Guid OrgId { get; init; }

    public required string TermsVersion { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset AcceptedAt { get; init; }

    public string? IpAddress { get; init; }
}

/// <summary>A member invitation; only the SHA-256 of the token is stored (task 005 security considerations).</summary>
internal sealed class Invitation
{
    public Guid Id { get; init; }

    public Guid OrgId { get; init; }

    public required string Email { get; init; }

    public required string TokenHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public Guid InvitedBy { get; init; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public Guid? AcceptedBy { get; set; }
}

/// <summary>The SEF connection of a seller (task 010). The API key is in the secret store under <see cref="SecretName"/>.</summary>
internal sealed class SefConnection
{
    public Guid OrgId { get; init; }

    public required string SecretName { get; init; }

    public SefConnectionStatus Status { get; set; }

    public string? SupplierPib { get; set; }

    public DateTimeOffset? ValidatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; }
}
