using Microsoft.Extensions.DependencyInjection;
using Modules.Audit;
using Modules.Audit.Contracts;
using Modules.Audit.Persistence;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Persistence;

namespace Platform.Integration.Tests;

/// <summary>Own database: tampering breaks the chain for every later test.</summary>
public sealed class AuditTamperTests(SqlServerContainer server) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Verification_detects_a_tampered_row()
    {
        await using (var scope = _database.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    for (var i = 0; i < 3; i++)
                    {
                        await audit.WriteAsync(new AuditEntry("Sample.Changed", "Sample", Guid.NewGuid(), Reason: "original"), ct);
                    }
                },
                TestContext.Current.CancellationToken);
        }

        // Only someone who can disable the trigger (ALTER permission, never the application) can do this.
        await _database.ExecuteAsync($"""
            DISABLE TRIGGER audit.{AuditDbContext.AuditEventsTrigger} ON audit.audit_events;
            UPDATE audit.audit_events SET reason = 'tampered' WHERE sequence = 2;
            ENABLE TRIGGER audit.{AuditDbContext.AuditEventsTrigger} ON audit.audit_events;
            """);

        await using var verifyScope = _database.Services.CreateAsyncScope();
        var verification = await verifyScope.ServiceProvider.GetRequiredService<AuditChainVerifier>().VerifyAsync(TestContext.Current.CancellationToken);

        Assert.False(verification.IsValid);
        Assert.Equal(2, verification.FirstInvalidSequence);
        Assert.Equal(1, verification.EventCount);
    }
}
