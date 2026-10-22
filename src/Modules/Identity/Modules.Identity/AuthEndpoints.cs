using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Modules.Identity.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Identity;

/// <summary>
/// <c>/api/v1/auth/*</c> and <c>/api/v1/me/*</c> (api.md, task 004). The sign-in and account-recovery endpoints are
/// not organization commands, so they take no <c>Idempotency-Key</c>; they are rate limited instead.
/// </summary>
public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public const string PendingPolicy = "pending-mfa";

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");
        var auth = api.MapGroup("/auth").RequireRateLimiting(RateLimitPolicy);

        auth.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(http).RequestToken }));
        auth.MapPost("/register", (RegisterRequest request, AuthService service, CancellationToken ct) => service.RegisterAsync(request, ct));
        auth.MapPost("/confirm-email", (ConfirmEmailRequest request, AuthService service, CancellationToken ct) => service.ConfirmEmailAsync(request, ct));
        auth.MapPost("/login", (HttpContext http, LoginRequest request, AuthService service, CancellationToken ct) => service.LoginAsync(http, request, ct));
        auth.MapPost("/password/forgot", (ForgotPasswordRequest request, AuthService service, CancellationToken ct) => service.ForgotPasswordAsync(request, ct));
        auth.MapPost("/password/reset", (ResetPasswordRequest request, AuthService service, CancellationToken ct) => service.ResetPasswordAsync(request, ct));

        var pending = auth.MapGroup("/mfa").RequireAuthorization(PendingPolicy);
        pending.MapPost("/enrol/start", async (HttpContext http, UserManager<AuthUser> users, AuthService service) =>
            await PendingUserAsync(http, users, AuthSchemes.EnrolStage) is { } user ? await service.StartEnrolmentAsync(user) : Results.Unauthorized());
        pending.MapPost("/enrol/confirm", async (HttpContext http, MfaCodeRequest request, UserManager<AuthUser> users, AuthService service, CancellationToken ct) =>
            await PendingUserAsync(http, users, AuthSchemes.EnrolStage) is { } user ? await service.ConfirmEnrolmentAsync(http, user, request, ct) : Results.Unauthorized());
        pending.MapPost("/verify", async (HttpContext http, MfaCodeRequest request, UserManager<AuthUser> users, AuthService service, CancellationToken ct) =>
            await PendingUserAsync(http, users, AuthSchemes.VerifyStage) is { } user ? await service.VerifyAsync(http, user, request, ct) : Results.Unauthorized());

        auth.MapPost("/step-up", async (HttpContext http, MfaCodeRequest request, UserManager<AuthUser> users, SessionStore sessions, AuthService service, CancellationToken ct) =>
            await CurrentAsync(http, users, sessions, ct) is var (user, session) ? await service.StepUpAsync(user, session, request, ct) : Results.Unauthorized())
            .RequireAuthorization();
        auth.MapPost("/mfa/provisioning", async (HttpContext http, UserManager<AuthUser> users, SessionStore sessions, AuthService service, CancellationToken ct) =>
            await CurrentAsync(http, users, sessions, ct) is var (user, _) ? await service.ShowProvisioningAsync(user, ct) : Results.Unauthorized())
            .RequireAuthorization()
            .RequireStepUp();
        auth.MapPost("/logout", async (HttpContext http, UserManager<AuthUser> users, SessionStore sessions, AuthService service, CancellationToken ct) =>
            await CurrentAsync(http, users, sessions, ct) is var (_, session) ? await service.LogoutAsync(http, session, ct) : Results.Unauthorized())
            .RequireAuthorization();

        var me = api.MapGroup("/me").RequireAuthorization();
        me.MapGet("", async (HttpContext http, UserManager<AuthUser> users, SessionStore sessions, CancellationToken ct) =>
            await CurrentAsync(http, users, sessions, ct) is var (user, _) ? Results.Ok(new { id = user.Id, email = user.Email }) : Results.Unauthorized());
        // Memberships come with task 005; reaching this endpoint already proves MFA (acceptance criterion of task 004).
        me.MapGet("/organizations", () => Results.Ok(Array.Empty<object>()));
        me.MapGet("/sessions", async (HttpContext http, SessionStore sessions, CancellationToken ct) =>
        {
            var (userId, sessionId) = Ids(http);
            var list = await sessions.ListActiveAsync(userId, ct);
            return Results.Ok(list.Select(s => new { id = s.Id, createdAt = s.CreatedAt, lastSeenAt = s.LastSeenAt, ipAddress = s.IpAddress, userAgent = s.UserAgent, current = s.Id == sessionId }));
        });
        me.MapDelete("/sessions/{id:guid}", (Guid id, HttpContext http, AuthService service, CancellationToken ct) =>
            service.RevokeSessionAsync(Ids(http).UserId, id, ct));
        // A sample step-up-protected action (task 004 acceptance); real ones arrive with their tasks.
        me.MapPost("/step-up-check", () => Results.NoContent()).RequireStepUp();

        return app;
    }

    private static (Guid UserId, Guid SessionId) Ids(HttpContext http) =>
        (Guid.Parse(http.User.FindFirst(CesioClaims.UserId)!.Value), Guid.Parse(http.User.FindFirst(CesioClaims.SessionId)!.Value));

    private static async Task<(AuthUser User, AuthSession Session)?> CurrentAsync(
        HttpContext http, UserManager<AuthUser> users, SessionStore sessions, CancellationToken cancellationToken)
    {
        var (userId, sessionId) = Ids(http);
        return await users.FindByIdAsync(userId.ToString()) is { } user
            && await sessions.FindActiveAsync(userId, sessionId, cancellationToken) is { } session
            ? (user, session)
            : null;
    }

    /// <summary>
    /// The user of the pending sign-in, if the cookie is for <paramref name="stage"/> and the user's state still fits it:
    /// the pending cookie lives 10 minutes, so an old "enrol" cookie must not work once MFA is enabled.
    /// </summary>
    private static async Task<AuthUser?> PendingUserAsync(HttpContext http, UserManager<AuthUser> users, string stage)
    {
        var result = await http.AuthenticateAsync(AuthSchemes.Pending);
        if (result.Principal?.FindFirst(AuthSchemes.StageClaim)?.Value != stage
            || result.Principal.FindFirst(CesioClaims.UserId)?.Value is not { } userId
            || await users.FindByIdAsync(userId) is not { } user)
        {
            return null;
        }

        return user.TwoFactorEnabled == (stage == AuthSchemes.VerifyStage) ? user : null;
    }
}
