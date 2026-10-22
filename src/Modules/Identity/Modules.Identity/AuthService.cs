using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Audit.Contracts;
using Modules.Identity.Persistence;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Persistence;
using Shared.Kernel;

namespace Modules.Identity;

public sealed record RegisterRequest(string Email, string Password);

public sealed record ConfirmEmailRequest(Guid UserId, string Token);

public sealed record LoginRequest(string Email, string Password);

/// <param name="Code">Authenticator code.</param>
/// <param name="RecoveryCode">One-time recovery code, instead of <paramref name="Code"/>.</param>
public sealed record MfaCodeRequest(string? Code, string? RecoveryCode = null);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

public sealed class AuthOptions
{
    /// <summary>Base URL of the SPA, used in email links.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";

    public int RateLimitPerMinute { get; set; } = 20;
}

/// <summary>
/// The authentication commands (task 004). Every write and its audit event run in one unit-of-work transaction
/// (audit.md §3: login success/failure, MFA enrolment, password reset, session revoke, lockout). Responses never
/// reveal whether an email is registered.
/// </summary>
internal sealed class AuthService(
    UserManager<AuthUser> users,
    AuthDbContext auth,
    SessionStore sessions,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IEmailSender email,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider)
{
    private const int RecoveryCodeCount = 10;
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly AuthUser DummyUser = new();

    /// <summary>A hash to verify against for unknown emails, so they cost as much as a wrong password.</summary>
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<AuthUser>().HashPassword(DummyUser, Convert.ToHexString(RandomNumberGenerator.GetBytes(16))));

    public async Task<IResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (!Input.IsEmail(request.Email) || !Input.IsPassword(request.Password))
        {
            return Problems.InvalidInput();
        }

        var user = new AuthUser { UserName = request.Email, Email = request.Email };
        // The password is validated before the email is looked up, so the answer never depends on the email.
        var passwordErrors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            passwordErrors.AddRange((await validator.ValidateAsync(users, user, request.Password)).Errors);
        }

        if (passwordErrors.Count > 0)
        {
            return Problems.Validation(passwordErrors);
        }

        if (await users.FindByEmailAsync(request.Email) is not null)
        {
            return Results.Accepted();
        }

        IdentityResult result = IdentityResult.Success;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                result = await users.CreateAsync(user, request.Password);
                if (result.Succeeded)
                {
                    await audit.WriteAsync(new AuditEntry("User.Registered", "User", user.Id), ct);
                }
            },
            cancellationToken);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName))
                ? Results.Accepted()
                : Problems.Validation(result.Errors);
        }

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await email.SendAsync(
            new EmailMessage(request.Email, "Potvrdite email adresu", $"Potvrdite email adresu za Cesio: {Link("confirm-email", ("userId", user.Id.ToString()), ("token", token))}"),
            cancellationToken);
        return Results.Accepted();
    }

    public async Task<IResult> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(request.UserId.ToString()) is not { } user)
        {
            return Problems.InvalidToken();
        }

        IdentityResult result = IdentityResult.Success;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                result = await users.ConfirmEmailAsync(user, request.Token);
                if (result.Succeeded)
                {
                    await audit.WriteAsync(new AuditEntry("User.EmailConfirmed", "User", user.Id), ct);
                }
            },
            cancellationToken);
        return result.Succeeded ? Results.NoContent() : Problems.InvalidToken();
    }

    public async Task<IResult> LoginAsync(HttpContext http, LoginRequest request, CancellationToken cancellationToken)
    {
        if (!Input.IsEmail(request.Email) || !Input.IsPassword(request.Password))
        {
            return Problems.InvalidCredentials();
        }

        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Same work as a wrong password, so the timing does not reveal the email. Not audited: unknown emails
            // are not an entity, and writing them to the hash chain would let anyone flood it.
            users.PasswordHasher.VerifyHashedPassword(DummyUser, DummyHash.Value, request.Password);
            return Problems.InvalidCredentials();
        }

        if (await users.IsLockedOutAsync(user))
        {
            await unitOfWork.ExecuteInTransactionAsync(
                ct => audit.WriteAsync(new AuditEntry("User.LoginFailed", "User", user.Id, Reason: "locked_out"), ct),
                cancellationToken);
            return Problems.InvalidCredentials();
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await RecordFailureAsync(user, "User.LoginFailed", "wrong_password", cancellationToken);
            return Problems.InvalidCredentials();
        }

        if (!user.EmailConfirmed)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "email_not_confirmed", "Confirm your email address first.");
        }

        // The failure count is not reset here but only after the second factor, so the lockout also covers MFA
        // guessing across repeated password sign-ins.
        var stage = user.TwoFactorEnabled ? AuthSchemes.VerifyStage : AuthSchemes.EnrolStage;
        await http.SignInAsync(AuthSchemes.Pending, SessionStore.PendingPrincipal(user.Id, stage));
        return Results.Ok(new { next = stage });
    }

    public async Task<IResult> StartEnrolmentAsync(AuthUser user)
    {
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }

        return Results.Ok(Provisioning(user, key!));
    }

    public async Task<IResult> ConfirmEnrolmentAsync(HttpContext http, AuthUser user, MfaCodeRequest request, CancellationToken cancellationToken)
    {
        if (await users.IsLockedOutAsync(user))
        {
            return Problems.InvalidCode();
        }

        if (!Input.IsCode(request.Code) || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.Code!))
        {
            await RecordFailureAsync(user, "User.MfaVerificationFailed", "enrolment_code", cancellationToken);
            return Problems.InvalidCode();
        }

        var codes = NewRecoveryCodes();
        var session = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await users.SetTwoFactorEnabledAsync(user, true);
                await users.ResetAccessFailedCountAsync(user);
                var now = timeProvider.GetUtcNow();
                auth.RecoveryCodes.AddRange(codes.Select(code => new RecoveryCode { Id = SequentialGuid.NewGuid(), UserId = user.Id, CodeHash = Hash(code), CreatedAt = now }));
                await auth.SaveChangesAsync(ct);
                // MFA change: every other session of the user ends (security.md §1).
                await sessions.RevokeAllAsync(user.Id, ct);
                var created = await sessions.CreateAsync(user.Id, ct);
                await audit.WriteAsync(new AuditEntry("User.MfaEnrolled", "User", user.Id), ct);
                await audit.WriteAsync(new AuditEntry("User.LoginSucceeded", "User", user.Id), ct);
                return created;
            },
            cancellationToken);

        await SignInAsync(http, user.Id, session.Id);
        return Results.Ok(new { recoveryCodes = codes });
    }

    public async Task<IResult> VerifyAsync(HttpContext http, AuthUser user, MfaCodeRequest request, CancellationToken cancellationToken)
    {
        if (await users.IsLockedOutAsync(user))
        {
            return Problems.InvalidCode();
        }

        Guid? recoveryCodeId = null;
        bool valid;
        if (request.RecoveryCode is { } recoveryCode)
        {
            recoveryCodeId = Input.IsCode(recoveryCode) ? await FindRecoveryCodeAsync(user.Id, recoveryCode, cancellationToken) : null;
            valid = recoveryCodeId is not null;
        }
        else
        {
            valid = Input.IsCode(request.Code) && await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.Code!);
        }

        if (!valid)
        {
            await RecordFailureAsync(user, "User.MfaVerificationFailed", request.RecoveryCode is null ? "code" : "recovery_code", cancellationToken);
            return Problems.InvalidCode();
        }

        var session = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                if (recoveryCodeId is { } id)
                {
                    // Claimed atomically: of two concurrent requests with the same code only one updates the row.
                    var now = timeProvider.GetUtcNow();
                    var claimed = await auth.RecoveryCodes
                        .Where(c => c.Id == id && c.UsedAt == null)
                        .ExecuteUpdateAsync(c => c.SetProperty(x => x.UsedAt, now), ct);
                    if (claimed != 1)
                    {
                        return null;
                    }

                    await audit.WriteAsync(new AuditEntry("User.RecoveryCodeUsed", "User", user.Id), ct);
                }

                await users.ResetAccessFailedCountAsync(user);
                var created = await sessions.CreateAsync(user.Id, ct);
                await audit.WriteAsync(new AuditEntry("User.LoginSucceeded", "User", user.Id), ct);
                return created;
            },
            cancellationToken);
        if (session is null)
        {
            return Problems.InvalidCode();
        }

        await SignInAsync(http, user.Id, session.Id);
        return Results.NoContent();
    }

    public async Task<IResult> StepUpAsync(AuthUser user, AuthSession session, MfaCodeRequest request, CancellationToken cancellationToken)
    {
        if (await users.IsLockedOutAsync(user))
        {
            return Problems.InvalidCode();
        }

        if (!Input.IsCode(request.Code) || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.Code!))
        {
            await RecordFailureAsync(user, "User.MfaVerificationFailed", "step_up", cancellationToken);
            return Problems.InvalidCode();
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await users.ResetAccessFailedCountAsync(user);
                session.MfaStepUpAt = timeProvider.GetUtcNow();
                await auth.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("User.StepUpCompleted", "User", user.Id), ct);
            },
            cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Shows the existing TOTP key again so another device can be enrolled (shared seller accounts, ADR-020).</summary>
    public async Task<IResult> ShowProvisioningAsync(AuthUser user, CancellationToken cancellationToken)
    {
        var key = await users.GetAuthenticatorKeyAsync(user);
        await unitOfWork.ExecuteInTransactionAsync(
            ct => audit.WriteAsync(new AuditEntry("User.MfaProvisioningShown", "User", user.Id), ct),
            cancellationToken);
        return Results.Ok(Provisioning(user, key!));
    }

    public async Task<IResult> LogoutAsync(HttpContext http, AuthSession session, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await sessions.RevokeAsync(session, ct);
                await audit.WriteAsync(new AuditEntry("User.LoggedOut", "User", session.UserId, Related: [new AuditReference("Session", session.Id)]), ct);
            },
            cancellationToken);
        await http.SignOutAsync(AuthSchemes.Session);
        return Results.NoContent();
    }

    public async Task<IResult> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (await sessions.FindActiveAsync(userId, sessionId, cancellationToken) is not { } session)
        {
            return Results.NotFound();
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await sessions.RevokeAsync(session, ct);
                await audit.WriteAsync(new AuditEntry("User.SessionRevoked", "User", userId, Related: [new AuditReference("Session", session.Id)]), ct);
            },
            cancellationToken);
        return Results.NoContent();
    }

    public async Task<IResult> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!Input.IsEmail(request.Email))
        {
            return Problems.InvalidInput();
        }

        if (await users.FindByEmailAsync(request.Email) is { EmailConfirmed: true } user)
        {
            // The link carries the user ID, not the email address (security.md §8: no personal data in URLs).
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await email.SendAsync(
                new EmailMessage(request.Email, "Promena lozinke", $"Novu lozinku za Cesio postavite ovde: {Link("reset-password", ("userId", user.Id.ToString()), ("token", token))}"),
                cancellationToken);
        }

        return Results.Accepted();
    }

    public async Task<IResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!Input.IsPassword(request.NewPassword) || string.IsNullOrEmpty(request.Token))
        {
            return Problems.InvalidInput();
        }

        if (await users.FindByIdAsync(request.UserId.ToString()) is not { } user)
        {
            return Problems.InvalidToken();
        }

        IdentityResult result = IdentityResult.Success;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
                if (result.Succeeded)
                {
                    await sessions.RevokeAllAsync(user.Id, ct);
                    await audit.WriteAsync(new AuditEntry("User.PasswordReset", "User", user.Id), ct);
                }
            },
            cancellationToken);
        if (result.Succeeded)
        {
            return Results.NoContent();
        }

        return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)) ? Problems.InvalidToken() : Problems.Validation(result.Errors);
    }

    /// <summary>Counts a failed password or code, audits it, and audits the lockout when it starts (5 failures).</summary>
    private async Task RecordFailureAsync(AuthUser user, string action, string reason, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await users.AccessFailedAsync(user);
                await audit.WriteAsync(new AuditEntry(action, "User", user.Id, Reason: reason), ct);
                if (await users.IsLockedOutAsync(user))
                {
                    await audit.WriteAsync(new AuditEntry("User.LockedOut", "User", user.Id), ct);
                }
            },
            cancellationToken);

        // Progressive delay on top of the lockout (security.md §1).
        await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Min(user.AccessFailedCount, 5)), timeProvider, cancellationToken);
    }

    private async Task<Guid?> FindRecoveryCodeAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var hash = Hash(code);
        return await auth.RecoveryCodes.AsNoTracking()
            .Where(c => c.UserId == userId && c.CodeHash == hash && c.UsedAt == null)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task SignInAsync(HttpContext http, Guid userId, Guid sessionId)
    {
        await http.SignOutAsync(AuthSchemes.Pending);
        await http.SignInAsync(AuthSchemes.Session, SessionStore.Principal(userId, sessionId));
    }

    private static object Provisioning(AuthUser user, string key) => new
    {
        sharedKey = key,
        otpauthUri = $"otpauth://totp/{Uri.EscapeDataString($"Cesio:{user.Email}")}?secret={key}&issuer=Cesio&digits=6",
    };

    private string Link(string path, params (string Name, string Value)[] query) =>
        $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{path}?{string.Join('&', query.Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value)}"))}";

    private static List<string> NewRecoveryCodes() =>
        [.. Enumerable.Range(0, RecoveryCodeCount).Select(_ =>
        {
            var chars = RandomNumberGenerator.GetItems<char>(RecoveryAlphabet, 10);
            return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
        })];

    /// <summary>SHA-256 of the code without dashes and in upper case, so users may type it either way.</summary>
    private static string Hash(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant())));
}

/// <summary>Length and shape limits on auth input; anything else is a 400 before any work is done.</summary>
internal static class Input
{
    private const int MaxEmailLength = 256;
    private const int MaxPasswordLength = 256;
    private const int MaxCodeLength = 32;

    public static bool IsEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= MaxEmailLength && email.Contains('@', StringComparison.Ordinal);

    public static bool IsPassword(string? password) => !string.IsNullOrEmpty(password) && password.Length <= MaxPasswordLength;

    public static bool IsCode(string? code) => !string.IsNullOrWhiteSpace(code) && code.Length <= MaxCodeLength;
}

internal static class Problems
{
    public static IResult InvalidInput() =>
        Error(StatusCodes.Status400BadRequest, "validation_failed", "The request is not valid.");

    public static IResult InvalidCredentials() =>
        Error(StatusCodes.Status401Unauthorized, "invalid_credentials", "The email or password is wrong, or the account is temporarily locked.");

    public static IResult InvalidCode() =>
        Error(StatusCodes.Status401Unauthorized, "invalid_code", "The code is wrong or has expired.");

    public static IResult InvalidToken() =>
        Error(StatusCodes.Status400BadRequest, "invalid_token", "The link is invalid or has expired.");

    public static IResult Validation(IEnumerable<IdentityError> errors) =>
        Results.Problem(
            detail: "The request is not valid.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = "validation_failed",
                ["errors"] = errors.Select(e => new { code = e.Code, message = e.Description }).ToList(),
            });

    public static IResult Error(int status, string errorCode, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
