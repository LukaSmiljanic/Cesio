using System.Net;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests;

/// <summary>Task 004: register → confirm → MFA → sign-in, step-up, lockout, CSRF, sessions, audit.</summary>
public sealed class AuthTests(SqlServerContainer server) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);
    private ApiApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _app = new ApiApp(_database);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task A_user_registers_confirms_enrols_mfa_and_reaches_the_api()
    {
        using var client = _app.NewClient();
        var (email, recoveryCodes) = await client.SignUpAsync();

        var me = await ApiClient.ReadAsync(await client.GetAsync("/api/v1/me"));
        using var organizations = await client.GetAsync("/api/v1/me/organizations");

        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.OK, organizations.StatusCode);
        Assert.Equal(10, recoveryCodes.Length);
        Assert.Equal(0, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM auth.recovery_codes WHERE code_hash LIKE '%-%'"));
    }

    [Fact]
    public async Task A_user_without_mfa_enrolment_cannot_reach_organizations()
    {
        using var client = _app.NewClient();
        await client.RegisterAndLoginAsync();

        using var organizations = await client.GetAsync("/api/v1/me/organizations");
        using var me = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, organizations.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Step_up_is_required_and_expires_after_five_minutes()
    {
        using var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();

        Assert.Equal(StepUp.ErrorCode, await ApiClient.ErrorCodeAsync(await client.PostAsync("/api/v1/me/step-up-check")));

        using (var stepUp = await client.PostAsync("/api/v1/auth/step-up", new { code = Totp.Code(client.SharedKey!, -1) }))
        {
            Assert.Equal(HttpStatusCode.NoContent, stepUp.StatusCode);
        }

        using (var allowed = await client.PostAsync("/api/v1/me/step-up-check"))
        {
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        await _database.ExecuteAsync($"""
            UPDATE s SET mfa_step_up_at = DATEADD(minute, -6, s.mfa_step_up_at)
            FROM auth.sessions s JOIN auth.users u ON u.id = s.user_id WHERE u.email = '{email}'
            """);
        using var expired = await client.PostAsync("/api/v1/me/step-up-check");

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        using var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();
        using var other = _app.NewClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("invalid_credentials", await ApiClient.ErrorCodeAsync(
                await other.PostAsync("/api/v1/auth/login", new { email, password = "wrong password, not the one" })));
        }

        using var correct = await other.PostAsync("/api/v1/auth/login", new { email, password = ApiClient.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.Equal(1, await AuditCountAsync(email, "User.LockedOut"));
    }

    [Fact]
    public async Task An_unsafe_request_without_the_antiforgery_header_is_rejected()
    {
        using var client = _app.NewClient();

        using var response = await client.Http.PostAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            System.Net.Http.Json.JsonContent.Create(new { email = "a@example.rs", password = ApiClient.Password }),
            TestContext.Current.CancellationToken);

        Assert.Equal("csrf_failed", await ApiClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_revoked_session_can_no_longer_be_used()
    {
        using var first = _app.NewClient();
        var (email, _) = await first.SignUpAsync();
        using var second = _app.NewClient();
        await second.SignInAsync(email, first.SharedKey!);

        var sessions = await ApiClient.ReadAsync(await first.GetAsync("/api/v1/me/sessions"));
        var otherSession = sessions.EnumerateArray().Single(s => !s.GetProperty("current").GetBoolean()).GetProperty("id").GetGuid();
        using (var revoke = await first.DeleteAsync($"/api/v1/me/sessions/{otherSession}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        using var afterRevoke = await second.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
        Assert.Equal(1, await AuditCountAsync(email, "User.SessionRevoked"));
    }

    [Fact]
    public async Task A_user_cannot_revoke_another_users_session()
    {
        using var alice = _app.NewClient();
        await alice.SignUpAsync();
        using var bob = _app.NewClient();
        await bob.SignUpAsync();
        var bobSession = (await ApiClient.ReadAsync(await bob.GetAsync("/api/v1/me/sessions"))).EnumerateArray().Single().GetProperty("id").GetGuid();

        using var response = await alice.DeleteAsync($"/api/v1/me/sessions/{bobSession}");
        using var bobStillSignedIn = await bob.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobStillSignedIn.StatusCode);
    }

    [Fact]
    public async Task Responses_do_not_reveal_whether_an_email_is_registered()
    {
        using var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();
        using var other = _app.NewClient();

        using var registerAgain = await other.PostAsync("/api/v1/auth/register", new { email, password = ApiClient.Password });
        var unknown = await ApiClient.ErrorCodeAsync(await other.PostAsync("/api/v1/auth/login", new { email = "nobody@example.rs", password = ApiClient.Password }));
        var wrong = await ApiClient.ErrorCodeAsync(await other.PostAsync("/api/v1/auth/login", new { email, password = "wrong password, not the one" }));
        using var forgotUnknown = await other.PostAsync("/api/v1/auth/password/forgot", new { email = "nobody@example.rs" });

        Assert.Equal(HttpStatusCode.Accepted, registerAgain.StatusCode);
        Assert.Equal(wrong, unknown);
        Assert.Equal(HttpStatusCode.Accepted, forgotUnknown.StatusCode);
    }

    [Fact]
    public async Task A_breached_or_short_password_is_rejected()
    {
        using var client = _app.NewClient();

        using var breached = await client.PostAsync("/api/v1/auth/register", new { email = "breached@example.rs", password = "password1234" });
        using var shortPassword = await client.PostAsync("/api/v1/auth/register", new { email = "short@example.rs", password = "short" });

        Assert.Contains("PasswordBreached", await breached.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once()
    {
        using var first = _app.NewClient();
        var (email, recoveryCodes) = await first.SignUpAsync();
        using var second = _app.NewClient();

        using (var login = await second.PostAsync("/api/v1/auth/login", new { email, password = ApiClient.Password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        using (var verify = await second.PostAsync("/api/v1/auth/mfa/verify", new { recoveryCode = recoveryCodes[0] }))
        {
            Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
        }

        using var third = _app.NewClient();
        using (var login = await third.PostAsync("/api/v1/auth/login", new { email, password = ApiClient.Password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        using var reused = await third.PostAsync("/api/v1/auth/mfa/verify", new { recoveryCode = recoveryCodes[0] });

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal(1, await AuditCountAsync(email, "User.RecoveryCodeUsed"));
    }

    [Fact]
    public async Task A_password_reset_signs_out_every_session_and_is_audited()
    {
        using var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();
        using var anonymous = _app.NewClient();
        using (var wrong = await anonymous.PostAsync("/api/v1/auth/login", new { email, password = "wrong password, not the one" }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        using (var forgot = await anonymous.PostAsync("/api/v1/auth/password/forgot", new { email }))
        {
            Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        }

        using (var reset = await anonymous.PostAsync("/api/v1/auth/password/reset", new
        {
            userId = Guid.Parse(_database.Emails.LinkValue(email, "userId")),
            token = _database.Emails.LinkValue(email, "token"),
            newPassword = "a brand new passphrase 2026",
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        using var oldSession = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        foreach (var action in new[] { "User.Registered", "User.EmailConfirmed", "User.MfaEnrolled", "User.LoginSucceeded", "User.LoginFailed", "User.PasswordReset" })
        {
            Assert.True(await AuditCountAsync(email, action) >= 1, $"Missing audit event {action}.");
        }
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        using var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();

        using (var logout = await client.PostAsync("/api/v1/auth/logout"))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        using var me = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(1, await AuditCountAsync(email, "User.LoggedOut"));
    }

    [Fact]
    public async Task The_provisioning_key_is_shown_again_only_after_step_up()
    {
        using var client = _app.NewClient();
        await client.SignUpAsync();

        Assert.Equal(StepUp.ErrorCode, await ApiClient.ErrorCodeAsync(await client.PostAsync("/api/v1/auth/mfa/provisioning")));
        using (var stepUp = await client.PostAsync("/api/v1/auth/step-up", new { code = Totp.Code(client.SharedKey!, 1) }))
        {
            Assert.Equal(HttpStatusCode.NoContent, stepUp.StatusCode);
        }

        var provisioning = await ApiClient.ReadAsync(await client.PostAsync("/api/v1/auth/mfa/provisioning"));

        Assert.Equal(client.SharedKey, provisioning.GetProperty("sharedKey").GetString());
        Assert.StartsWith("otpauth://totp/", provisioning.GetProperty("otpauthUri").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guessing_mfa_codes_across_password_sign_ins_still_locks_the_account()
    {
        using var owner = _app.NewClient();
        var (email, _) = await owner.SignUpAsync();
        using var attacker = _app.NewClient();

        for (var round = 0; round < 2; round++)
        {
            using (var login = await attacker.PostAsync("/api/v1/auth/login", new { email, password = ApiClient.Password }))
            {
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            }

            for (var i = 0; i < 3; i++)
            {
                using var wrong = await attacker.PostAsync("/api/v1/auth/mfa/verify", new { code = "000000" });
                Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            }
        }

        using var correct = await attacker.PostAsync("/api/v1/auth/mfa/verify", new { code = Totp.Code(owner.SharedKey!, 1) });

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.Equal(1, await AuditCountAsync(email, "User.LockedOut"));
    }

    [Fact]
    public async Task An_old_enrolment_sign_in_stops_working_once_mfa_is_enabled()
    {
        using var first = _app.NewClient();
        var email = await first.RegisterAndLoginAsync();
        using var second = _app.NewClient();
        using (var login = await second.PostAsync("/api/v1/auth/login", new { email, password = ApiClient.Password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var start = await ApiClient.ReadAsync(await first.PostAsync("/api/v1/auth/mfa/enrol/start"));
        using (var confirm = await first.PostAsync("/api/v1/auth/mfa/enrol/confirm", new { code = Totp.Code(start.GetProperty("sharedKey").GetString()!) }))
        {
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        }

        using var staleStart = await second.PostAsync("/api/v1/auth/mfa/enrol/start");

        Assert.Equal(HttpStatusCode.Unauthorized, staleStart.StatusCode);
        Assert.Equal(10, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM auth.recovery_codes r JOIN auth.users u ON u.id = r.user_id WHERE u.email = '{email}'"));
    }

    [Fact]
    public async Task A_recovery_code_used_concurrently_signs_in_once()
    {
        using var owner = _app.NewClient();
        var (email, recoveryCodes) = await owner.SignUpAsync();
        var clients = Enumerable.Range(0, 4).Select(_ => _app.NewClient()).ToList();
        foreach (var client in clients)
        {
            using var login = await client.PostAsync("/api/v1/auth/login", new { email, password = ApiClient.Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var responses = await Task.WhenAll(clients.Select(c => c.PostAsync("/api/v1/auth/mfa/verify", new { recoveryCode = recoveryCodes[1] })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        clients.ForEach(c => c.Dispose());
    }

    [Theory]
    [InlineData("last_seen_at = DATEADD(minute, -31, SYSDATETIMEOFFSET())")]
    [InlineData("expires_at = DATEADD(minute, -1, SYSDATETIMEOFFSET())")]
    public async Task A_session_ends_after_the_idle_or_absolute_limit(string expire)
    {
        using var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();

        await _database.ExecuteAsync($"UPDATE s SET {expire} FROM auth.sessions s JOIN auth.users u ON u.id = s.user_id WHERE u.email = '{email}'");
        using var me = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task A_wrong_email_confirmation_token_is_rejected()
    {
        using var client = _app.NewClient();
        var email = $"user-{Guid.NewGuid():N}@example.rs";
        using (var register = await client.PostAsync("/api/v1/auth/register", new { email, password = ApiClient.Password }))
        {
            Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        }

        var code = await ApiClient.ErrorCodeAsync(await client.PostAsync("/api/v1/auth/confirm-email", new
        {
            userId = Guid.Parse(_database.Emails.LinkValue(email, "userId")),
            token = "not-the-token",
        }));

        Assert.Equal("invalid_token", code);
    }

    private Task<int> AuditCountAsync(string email, string action) =>
        _database.ScalarAsync<int>($"""
            SELECT COUNT(*) FROM audit.audit_events a JOIN auth.users u ON u.id = a.entity_id
            WHERE u.email = '{email}' AND a.action = '{action}'
            """);
}

