using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Identity.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Identity;

/// <summary>Authentication schemes: the full session after MFA, and the short pending sign-in between password and MFA.</summary>
public static class AuthSchemes
{
    public const string Session = "Cesio.Session";

    public const string Pending = "Cesio.Pending";

    /// <summary>Claim on the pending sign-in: <see cref="EnrolStage"/> or <see cref="VerifyStage"/>.</summary>
    public const string StageClaim = "stage";

    public const string EnrolStage = "enrol";

    public const string VerifyStage = "verify";
}

/// <summary>Session lifetime rules (security.md §1).</summary>
internal static class SessionPolicy
{
    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan Absolute = TimeSpan.FromHours(12);

    /// <summary>Last-seen updates are throttled, so not every request writes.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Pending = TimeSpan.FromMinutes(10);
}

internal sealed class SessionStore(AuthDbContext auth, TimeProvider timeProvider, IRequestContext request)
{
    public async Task<AuthSession> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var session = new AuthSession
        {
            Id = SequentialGuid.NewGuid(),
            UserId = userId,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + SessionPolicy.Absolute,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
        };
        auth.Sessions.Add(session);
        await auth.SaveChangesAsync(cancellationToken);
        return session;
    }

    public Task<AuthSession?> FindActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        auth.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null, cancellationToken);

    public async Task<List<AuthSession>> ListActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var idleSince = now - SessionPolicy.Idle;
        return await auth.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now && s.LastSeenAt > idleSince)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeAsync(AuthSession session, CancellationToken cancellationToken)
    {
        session.RevokedAt = timeProvider.GetUtcNow();
        await auth.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Revokes every session of the user (password reset, MFA change).</summary>
    public Task<int> RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return auth.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
    }

    public static ClaimsPrincipal Principal(Guid userId, Guid sessionId) =>
        new(new ClaimsIdentity(
            [
                new Claim(CesioClaims.UserId, userId.ToString()),
                new Claim(CesioClaims.SessionId, sessionId.ToString()),
                new Claim(CesioClaims.AuthenticationMethod, CesioClaims.Mfa),
            ],
            AuthSchemes.Session,
            CesioClaims.UserId,
            roleType: null));

    public static ClaimsPrincipal PendingPrincipal(Guid userId, string stage) =>
        new(new ClaimsIdentity(
            [new Claim(CesioClaims.UserId, userId.ToString()), new Claim(AuthSchemes.StageClaim, stage)],
            AuthSchemes.Pending,
            CesioClaims.UserId,
            roleType: null));

    /// <summary>
    /// Cookie validation on every request: the session must exist, not be revoked, and be within the idle and
    /// absolute limits. Exposes the step-up time to <see cref="StepUp.RequireStepUp{TBuilder}"/> and the audit.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            || !Guid.TryParse(principal.FindFirst(CesioClaims.SessionId)?.Value, out var sessionId))
        {
            await RejectAsync(context);
            return;
        }

        var services = context.HttpContext.RequestServices;
        var auth = services.GetRequiredService<AuthDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var session = await auth.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, context.HttpContext.RequestAborted);
        if (session is null || session.RevokedAt is not null || now >= session.ExpiresAt || now - session.LastSeenAt > SessionPolicy.Idle)
        {
            await RejectAsync(context);
            return;
        }

        if (now - session.LastSeenAt > SessionPolicy.TouchInterval)
        {
            session.LastSeenAt = now;
            await auth.SaveChangesAsync(context.HttpContext.RequestAborted);
        }

        if (session.MfaStepUpAt is { } stepUpAt)
        {
            // After an MFA reset the account cannot step up for a while (security.md §1), whatever the session says.
            var blockedUntil = await auth.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.StepUpBlockedUntil).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (blockedUntil is null || blockedUntil <= now)
            {
                context.HttpContext.Items[CesioClaims.MfaStepUpAtItem] = stepUpAt;
            }
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(AuthSchemes.Session);
    }
}
