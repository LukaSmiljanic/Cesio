namespace Modules.Identity.Contracts;

/// <summary>Read access to users for other modules (e.g. matching an invitation to the signed-in user).</summary>
public interface IUserDirectory
{
    /// <summary>The user's email, or null when the user does not exist.</summary>
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Platform staff roles (security.md §3). Until the staff realm (task 007) they are user claims.</summary>
public static class PlatformRoles
{
    public const string ComplianceOfficer = "ComplianceOfficer";

    /// <summary>Platform operations staff, e.g. suspending a listing (task 012).</summary>
    public const string Operations = "Operations";

    /// <summary>Fee schedules (task 017a); four-eyes between two Finance users.</summary>
    public const string Finance = "Finance";

    /// <summary>Platform configuration such as the signature trust store (task 016); no compliance decisions (security.md).</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>Reads the audit log (task 022); no other rights.</summary>
    public const string Auditor = "Auditor";
}

public interface IPlatformRoles
{
    Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken cancellationToken);

    /// <summary>Whether the user is platform staff (any role); staff cannot belong to customer organizations (task 007).</summary>
    Task<bool> IsStaffAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Executes an approved MFA reset (task 007a, security.md §1); the request and its approvals live in Organizations.</summary>
public interface IMfaReset
{
    /// <summary>
    /// Removes the authenticator and the recovery codes, ends every session (the next sign-in enrols MFA again) and blocks
    /// step-up until <paramref name="stepUpBlockedUntil"/>.
    /// </summary>
    Task ResetAsync(Guid userId, DateTimeOffset stepUpBlockedUntil, CancellationToken cancellationToken);
}

/// <summary>Authorization and rate-limit policy names other modules use on authentication-adjacent endpoints.</summary>
public static class AuthPolicies
{
    /// <summary>A user who passed the password but not yet MFA.</summary>
    public const string PendingMfa = "pending-mfa";

    /// <summary>A user who passed the password and must now pass MFA (MFA is enrolled), e.g. to ask for an MFA reset.</summary>
    public const string PendingMfaVerify = "pending-mfa-verify";

    /// <summary>The per-IP limit of the sign-in endpoints.</summary>
    public const string RateLimit = "auth";
}
