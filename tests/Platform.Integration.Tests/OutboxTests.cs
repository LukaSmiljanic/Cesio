using Microsoft.Extensions.DependencyInjection;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;

namespace Platform.Integration.Tests;

public sealed class OutboxTests(SqlServerContainer server) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task An_event_reaches_an_idempotent_consumer_once_despite_a_crash_after_publish()
    {
        var eventId = await PublishAsync();
        var before = _database.ConsumedEvents.Count;
        var dispatcher = _database.Services.GetRequiredService<OutboxDispatcher>();

        await dispatcher.DispatchOnceAsync(TestContext.Current.CancellationToken);
        // Simulate a crash after the consumer committed but before the message was marked processed.
        await _database.ExecuteAsync($"UPDATE platform.outbox SET processed_at = NULL, next_attempt_at = SYSDATETIMEOFFSET() WHERE id = '{eventId}'");
        await dispatcher.DispatchOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(before + 1, _database.ConsumedEvents.Count);
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM platform.processed_events WHERE event_id = '{eventId}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM platform.outbox WHERE id = '{eventId}' AND processed_at IS NOT NULL"));
    }

    [Fact]
    public async Task A_failing_consumer_is_retried_and_then_dead_lettered()
    {
        await using var services = _database.BuildServices(
            s => s.AddScoped<Shared.Infrastructure.Requests.IRequestContext>(_ => _database.RequestContext),
            new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "2" });
        var eventId = await PublishAsync();
        _database.ConsumedEvents.Failure = () => new InvalidOperationException("consumer down");
        try
        {
            var dispatcher = services.GetRequiredService<OutboxDispatcher>();
            await dispatcher.DispatchOnceAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT attempts FROM platform.outbox WHERE id = '{eventId}'"));

            await _database.ExecuteAsync($"UPDATE platform.outbox SET next_attempt_at = SYSDATETIMEOFFSET() WHERE id = '{eventId}'");
            await dispatcher.DispatchOnceAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            _database.ConsumedEvents.Failure = () => null;
        }

        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM platform.outbox WHERE id = '{eventId}' AND dead_lettered_at IS NOT NULL AND processed_at IS NULL"));
    }

    private async Task<Guid> PublishAsync()
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        var orderId = Guid.NewGuid();
        await unitOfWork.ExecuteInTransactionAsync(ct => outbox.AddAsync(new SampleOrderSubmitted(orderId), ct), TestContext.Current.CancellationToken);
        return await _database.ScalarAsync<Guid>($"SELECT id FROM platform.outbox WHERE payload LIKE '%{orderId}%'");
    }
}
