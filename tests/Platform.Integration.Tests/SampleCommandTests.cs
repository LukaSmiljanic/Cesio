using Microsoft.Extensions.DependencyInjection;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Persistence;

namespace Platform.Integration.Tests;

/// <summary>Task 003 acceptance: a state change, its audit event and its outbox message commit or roll back together.</summary>
public sealed class SampleCommandTests(SqlServerContainer server) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task State_change_audit_and_outbox_commit_together()
    {
        var orderId = await CreateOrderAsync();

        await using (var scope = _database.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SubmitSampleOrder>().ExecuteAsync(orderId, afterWrites: null, TestContext.Current.CancellationToken);
        }

        Assert.Equal("Submitted", await _database.ScalarAsync<string>($"SELECT state FROM sample.orders WHERE id = '{orderId}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"""
            SELECT COUNT(*) FROM audit.audit_events
            WHERE entity_id = '{orderId}' AND action = 'SampleOrder.Submitted' AND previous_state = 'Draft' AND new_state = 'Submitted'
            """));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM platform.outbox WHERE payload LIKE '%{orderId}%'"));
    }

    [Fact]
    public async Task A_failure_after_the_writes_rolls_all_three_back()
    {
        var orderId = await CreateOrderAsync();

        await using (var scope = _database.Services.CreateAsyncScope())
        {
            var command = scope.ServiceProvider.GetRequiredService<SubmitSampleOrder>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(
                orderId, () => throw new InvalidOperationException("crash before commit"), TestContext.Current.CancellationToken));
        }

        Assert.Equal("Draft", await _database.ScalarAsync<string>($"SELECT state FROM sample.orders WHERE id = '{orderId}'"));
        Assert.Equal(0, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM audit.audit_events WHERE entity_id = '{orderId}'"));
        Assert.Equal(0, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM platform.outbox WHERE payload LIKE '%{orderId}%'"));
    }

    private async Task<Guid> CreateOrderAsync()
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        var sample = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        var order = new SampleOrder { Id = Guid.NewGuid() };
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                sample.Orders.Add(order);
                await sample.SaveChangesAsync(ct);
            },
            TestContext.Current.CancellationToken);
        return order.Id;
    }
}
