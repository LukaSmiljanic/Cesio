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
}

public interface IPlatformRoles
{
    Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken cancellationToken);
}
