using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Integrations.Sef;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Secrets;

namespace Platform.Integration.Tests;

/// <summary>Task 023: security headers, no secrets or personal data in logs, and the database grants of the application role.</summary>
public sealed class SecurityHardeningTests(SqlServerContainer server) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);
    private readonly CapturingLoggerProvider _logs = new();
    private ApiApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _app = new ApiApp(_database, new Dictionary<string, string?> { ["Sef:Client"] = "Fake" },
            services => services.AddSingleton<ILoggerProvider>(_logs));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        using var client = _app.NewClient();
        using var response = await client.GetAsync("/api/v1/auth/antiforgery");

        Assert.Equal("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single(), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Logs_contain_no_emails_keys_tokens_or_account_numbers()
    {
        using var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();
        var pib = TestIds.NewPib();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/organizations", new { pib }))).GetProperty("id").GetGuid();
        var key = $"sef-secret-{Guid.NewGuid():N}";
        _app.Services.GetRequiredService<FakeSefClient>().AddInvoice(key, FakeSefClient.Ubl(pib, "106006802", "L-1", 10m, 2m));
        await client.StepUpAsync();
        using (var connected = await client.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/sef-connection", new { apiKey = key }))
        {
            Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        }

        var account = TestIds.NewBankAccount();
        using (var added = await client.PostAsync($"/api/v1/organizations/{org}/bank-accounts", new { accountNumber = account }))
        {
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        using (var failed = await client.PostAsync("/api/v1/auth/login", new { email, password = "wrong password 123456" }))
        {
            Assert.NotEqual(HttpStatusCode.OK, failed.StatusCode);
        }

        var text = string.Join("\n", _logs.Messages);
        Assert.NotEmpty(_logs.Messages);
        Assert.DoesNotContain(email, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(key, text, StringComparison.Ordinal);
        Assert.DoesNotContain(account, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiClient.Password, text, StringComparison.Ordinal);

        // ADR-023: the SEF key is stored only encrypted.
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM platform.secrets WHERE name = 'sef-key-{org:N}'"));
        Assert.Equal(0, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM platform.secrets WHERE ciphertext LIKE '%{key}%'"));
    }

    [Fact]
    public async Task A_secret_copied_to_another_name_does_not_decrypt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
            await secrets.SetAsync("sef-key-a", "key of organization A", ct);
            await secrets.SetAsync("sef-key-b", "key of organization B", ct);
            Assert.Equal("key of organization A", await secrets.GetAsync("sef-key-a", ct));
        }

        await using (var connection = new SqlConnection(_database.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await ExecAsync(connection, "UPDATE platform.secrets SET ciphertext = (SELECT ciphertext FROM platform.secrets WHERE name = 'sef-key-a') WHERE name = 'sef-key-b'");
        }

        await using var readScope = _app.Services.CreateAsyncScope();
        await Assert.ThrowsAnyAsync<CryptographicException>(() => readScope.ServiceProvider.GetRequiredService<ISecretStore>().GetAsync("sef-key-b", ct));
    }

    [Fact]
    public async Task The_application_role_cannot_change_audit_rows_or_delete_fee_charges()
    {
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await ExecAsync(connection, "CREATE USER grant_probe WITHOUT LOGIN; ALTER ROLE app_rw ADD MEMBER grant_probe;");

        var auditUpdate = await TryAsync(connection, "EXECUTE AS USER = 'grant_probe'; UPDATE audit.audit_events SET reason = 'x'; REVERT;");
        var auditDelete = await TryAsync(connection, "EXECUTE AS USER = 'grant_probe'; DELETE FROM audit.audit_events; REVERT;");
        var feeDelete = await TryAsync(connection, "EXECUTE AS USER = 'grant_probe'; DELETE FROM fees.fee_charges; REVERT;");
        var auditReadAllowed = await TryAsync(connection, "EXECUTE AS USER = 'grant_probe'; SELECT COUNT(*) FROM audit.audit_events; REVERT;");

        Assert.Contains("permission was denied", auditUpdate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("permission was denied", auditDelete, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("permission was denied", feeDelete, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, auditReadAllowed);
    }

    [Fact]
    public async Task The_migrator_creates_the_application_login_in_app_rw_and_rotates_its_password()
    {
        var login = $"cesio_app_{Guid.NewGuid():N}"[..24];
        const string first = "Prva'Lozinka-2026!x";
        const string second = "Druga-Lozinka-2026!y";
        try
        {
            await DatabaseMigrator.AddApplicationLoginAsync(_database.ConnectionString, login, first, TestContext.Current.CancellationToken);
            var asApp = new SqlConnectionStringBuilder(_database.ConnectionString) { UserID = login, Password = first }.ConnectionString;
            var denied = await TryAsAsync(asApp, "DELETE FROM fees.fee_charges;");
            var allowed = await TryAsAsync(asApp, "SELECT COUNT(*) FROM audit.audit_events;");

            await DatabaseMigrator.AddApplicationLoginAsync(_database.ConnectionString, login, second, TestContext.Current.CancellationToken);
            var oldPassword = await TryAsAsync(asApp, "SELECT 1;");
            var newPassword = await TryAsAsync(new SqlConnectionStringBuilder(asApp) { Password = second }.ConnectionString, "SELECT 1;");

            Assert.Contains("permission was denied", denied, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, allowed);
            Assert.Contains("Login failed", oldPassword, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, newPassword);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(_database.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await ExecAsync(connection, $"IF DATABASE_PRINCIPAL_ID('{login}') IS NOT NULL DROP USER [{login}]; IF SUSER_ID('{login}') IS NOT NULL DROP LOGIN [{login}];");
        }
    }

    /// <summary>The SQL error message as that login, or empty when the statement ran.</summary>
    private static async Task<string> TryAsAsync(string connectionString, string sql)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await ExecAsync(connection, sql);
            return string.Empty;
        }
        catch (SqlException exception)
        {
            return exception.Message;
        }
    }

    private static async Task ExecAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The SQL error message, or empty when the statement ran.</summary>
    private static async Task<string> TryAsync(SqlConnection connection, string sql)
    {
        try
        {
            await ExecAsync(connection, sql);
            return string.Empty;
        }
        catch (SqlException exception)
        {
            await ExecAsync(connection, "IF USER_NAME() = 'grant_probe' REVERT;");
            return exception.Message;
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Messages);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }
}
