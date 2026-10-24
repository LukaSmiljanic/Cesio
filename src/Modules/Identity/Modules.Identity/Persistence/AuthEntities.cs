using Microsoft.AspNetCore.Identity;
using Shared.Kernel;

namespace Modules.Identity.Persistence;

/// <summary>A natural person who signs in (domain-model.md §3 User). User name = email.</summary>
public sealed class AuthUser : IdentityUser<Guid>
{
    public AuthUser() => Id = SequentialGuid.NewGuid();

    /// <summary>After an MFA reset, step-up is refused until then (security.md §1, task 007a).</summary>
    public DateTimeOffset? StepUpBlockedUntil { get; set; }
}

/// <summary>
/// One signed-in browser session (security.md §1): idle timeout from <see cref="LastSeenAt"/>, absolute timeout at
/// <see cref="ExpiresAt"/>, remote sign-out through <see cref="RevokedAt"/>, and the last step-up time.
/// </summary>
internal sealed class AuthSession
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? MfaStepUpAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }
}

/// <summary>A one-time MFA recovery code; only its SHA-256 hash is stored (security.md §1).</summary>
internal sealed class RecoveryCode
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public required string CodeHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UsedAt { get; set; }
}
