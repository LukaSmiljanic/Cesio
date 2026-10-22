using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Modules.Audit;
using Modules.Audit.Contracts;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Persistence;

namespace Platform.Integration.Tests;

public sealed class AuditTests(SqlServerContainer server) : IAsyncLifetime
{
    /// <summary>229: permission denied (UPDATE, DELETE); 1088 / 4701: TRUNCATE without ALTER permission.</summary>
    private static readonly int[] PermissionErrors = [229, 1088, 4701];

    private readonly TestDatabase _database = new(server);

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Events_form_a_verifiable_hash_chain()
    {
        await WriteAsync("Sample.First", "Sample.Second");
        await WriteAsync("Sample.Third");

        var verification = await VerifyAsync();

        Assert.True(verification.IsValid);
        Assert.Equal(3, verification.EventCount);
        Assert.Equal(3, verification.LastSequence);
        Assert.Equal(verification.LastHash, await _database.ScalarAsync<string>("SELECT hash FROM audit.audit_events WHERE sequence = 3"));
    }

    [Fact]
    public async Task A_business_rollback_leaves_no_audit_row()
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        var entityId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await audit.WriteAsync(new AuditEntry("Sample.RolledBack", "Sample", entityId), ct);
                throw new InvalidOperationException("The business change failed.");
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(0, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{entityId}'"));
    }

    [Fact]
    public async Task Writing_outside_a_transaction_fails()
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            audit.WriteAsync(new AuditEntry("Sample.NoTransaction", "Sample", Guid.NewGuid()), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("UPDATE audit.audit_events SET reason = 'changed'")]
    [InlineData("DELETE FROM audit.audit_events")]
    [InlineData("UPDATE audit.access_log SET action = 'changed'")]
    [InlineData("DELETE FROM audit.access_log")]
    public async Task Update_and_delete_fail_even_for_the_owner(string sql)
    {
        await WriteAsync("Sample.Protected");
        await RecordAccessAsync();

        var exception = await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync(sql));

        Assert.Contains("append-only", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UPDATE audit.audit_events SET reason = 'changed'")]
    [InlineData("DELETE FROM audit.audit_events")]
    [InlineData("TRUNCATE TABLE audit.audit_events")]
    public async Task The_application_role_cannot_change_audit_rows(string sql)
    {
        await WriteAsync("Sample.ProtectedFromApp");
        var user = $"app_{Guid.NewGuid():N}";
        await _database.ExecuteAsync($"CREATE USER [{user}] WITHOUT LOGIN; ALTER ROLE app_rw ADD MEMBER [{user}];");

        var exception = await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync($"EXECUTE AS USER = '{user}'; {sql}; REVERT;"));

        Assert.Contains(exception.Number, PermissionErrors);
    }

    [Fact]
    public async Task Running_the_migrator_again_changes_nothing()
    {
        await WriteAsync("Sample.BeforeRerun");

        await DatabaseMigrator.MigrateAsync(_database.Services, _database.ConnectionString, TestContext.Current.CancellationToken);

        Assert.True((await VerifyAsync()).IsValid);
    }

    [Fact]
    public async Task The_application_role_can_insert_and_read_audit_rows()
    {
        var user = $"app_{Guid.NewGuid():N}";
        await _database.ExecuteAsync($"CREATE USER [{user}] WITHOUT LOGIN; ALTER ROLE app_rw ADD MEMBER [{user}];");

        await _database.ExecuteAsync($"""
            EXECUTE AS USER = '{user}';
            INSERT INTO audit.access_log (id, occurred_at, actor_type, action, entity_type, entity_id, correlation_id)
            VALUES (NEWID(), SYSDATETIMEOFFSET(), 'User', 'Sample.Viewed', 'Sample', NEWID(), 'test');
            SELECT COUNT(*) FROM audit.access_log;
            REVERT;
            """);
    }

    private async Task WriteAsync(params string[] actions)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                foreach (var action in actions)
                {
                    await audit.WriteAsync(
                        new AuditEntry(action, "Sample", Guid.NewGuid(), Changes: new Dictionary<string, string?> { ["amount"] = "97000.00 RSD" }, Reason: "test"),
                        ct);
                }
            },
            TestContext.Current.CancellationToken);
    }

    private async Task RecordAccessAsync()
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        var accessLog = scope.ServiceProvider.GetRequiredService<IAccessLog>();
        await unitOfWork.ExecuteInTransactionAsync(
            ct => accessLog.RecordAsync(new AccessLogEntry("Sample.Viewed", "Sample", Guid.NewGuid()), ct),
            TestContext.Current.CancellationToken);
    }

    private async Task<AuditChainVerification> VerifyAsync()
    {
        await using var scope = _database.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuditChainVerifier>().VerifyAsync(TestContext.Current.CancellationToken);
    }
}
