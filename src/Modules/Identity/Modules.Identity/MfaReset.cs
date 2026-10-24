using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Identity.Contracts;
using Modules.Identity.Persistence;

namespace Modules.Identity;

/// <summary><see cref="IMfaReset"/>: the Identity side of an approved MFA reset (task 007a).</summary>
internal sealed class MfaReset(UserManager<AuthUser> users, AuthDbContext auth, IAuditWriter audit, TimeProvider timeProvider) : IMfaReset
{
    public async Task ResetAsync(Guid userId, DateTimeOffset stepUpBlockedUntil, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("Unknown user.");
        user.StepUpBlockedUntil = stepUpBlockedUntil;
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        var now = timeProvider.GetUtcNow();
        await auth.RecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await auth.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
        await audit.WriteAsync(new AuditEntry("User.MfaReset", "User", userId,
            Changes: new Dictionary<string, string?> { ["stepUpBlockedUntil"] = stepUpBlockedUntil.ToString("O") }), cancellationToken);
    }
}
