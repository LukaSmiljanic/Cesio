using System.Diagnostics.Metrics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;

namespace Shared.Infrastructure.Outbox;

/// <summary>
/// Delivers outbox messages at least once (database.md §4). A batch is claimed with
/// <c>WITH (UPDLOCK, READPAST, ROWLOCK)</c> and leased, so several dispatchers never take the same message. Each
/// consumer runs in its own transaction together with a <c>processed_events</c> row, so a redelivered message does not
/// repeat a consumer's side effects.
/// </summary>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IEnumerable<OutboxConsumer> consumers,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    public const string MeterName = "Cesio.Outbox";

    private const int MaxErrorLength = 2000;

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Delivered = Meter.CreateCounter<long>("outbox.delivered");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("outbox.failed");
    private static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("outbox.dead_lettered");

    private readonly ILookup<string, OutboxConsumer> _consumers = consumers.ToLookup(consumer => consumer.EventType);

    /// <summary>Claims and processes one batch; returns the number of messages claimed.</summary>
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        var batch = await ClaimBatchAsync(cancellationToken);
        foreach (var message in batch)
        {
            await ProcessAsync(message, cancellationToken);
        }

        return batch.Count;
    }

    private async Task<List<ClaimedMessage>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = new SqlConnection(PersistenceSetup.ConnectionString(configuration));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            WITH batch AS (
              SELECT TOP (@batchSize) id, type, payload, attempts, next_attempt_at
              FROM platform.outbox WITH (UPDLOCK, READPAST, ROWLOCK)
              WHERE processed_at IS NULL AND dead_lettered_at IS NULL AND next_attempt_at <= @now
              ORDER BY next_attempt_at)
            UPDATE batch SET next_attempt_at = @leaseUntil
            OUTPUT inserted.id, inserted.type, inserted.payload, inserted.attempts;
            """,
            connection);
        command.Parameters.AddWithValue("@batchSize", options.Value.BatchSize);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@leaseUntil", now + options.Value.ClaimLease);

        var batch = new List<ClaimedMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            batch.Add(new ClaimedMessage(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        }

        return batch;
    }

    private async Task ProcessAsync(ClaimedMessage message, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var consumer in _consumers[message.Type])
            {
                await DeliverAsync(message, consumer, cancellationToken);
            }

            await CompleteAsync(message, cancellationToken);
            Delivered.Add(1);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await FailAsync(message, exception, cancellationToken);
        }
    }

    private async Task DeliverAsync(ClaimedMessage message, OutboxConsumer consumer, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                if (await platform.ProcessedEvents.AnyAsync(p => p.EventId == message.Id && p.Consumer == consumer.Consumer, ct))
                {
                    return;
                }

                platform.ProcessedEvents.Add(new ProcessedEvent { EventId = message.Id, Consumer = consumer.Consumer, ProcessedAt = timeProvider.GetUtcNow() });
                await platform.SaveChangesAsync(ct);
                await consumer.Handle(scope.ServiceProvider, message.Payload, message.Id, ct);
            },
            cancellationToken);
    }

    private async Task CompleteAsync(ClaimedMessage message, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await platform.OutboxMessages
            .Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.ProcessedAt, timeProvider.GetUtcNow()), cancellationToken);
    }

    private async Task FailAsync(ClaimedMessage message, Exception exception, CancellationToken cancellationToken)
    {
        var attempts = message.Attempts + 1;
        var now = timeProvider.GetUtcNow();
        var deadLetter = attempts >= options.Value.MaxAttempts;
        var backoff = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempts), options.Value.MaxBackoff.TotalSeconds));
        var error = $"{exception.GetType().Name}: {exception.Message}";
        error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        await using var scope = scopeFactory.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await platform.OutboxMessages
            .Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(
                m => m
                    .SetProperty(x => x.Attempts, attempts)
                    .SetProperty(x => x.LastError, error)
                    .SetProperty(x => x.NextAttemptAt, now + backoff)
                    .SetProperty(x => x.DeadLetteredAt, deadLetter ? now : null),
                cancellationToken);

        if (deadLetter)
        {
            DeadLettered.Add(1);
            LogDeadLettered(logger, exception, message.Id, message.Type, attempts);
        }
        else
        {
            Failed.Add(1);
            LogFailed(logger, exception, message.Id, message.Type, attempts);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) failed on attempt {Attempts}; it will be retried.")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid messageId, string type, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) was dead-lettered after {Attempts} attempts.")]
    private static partial void LogDeadLettered(ILogger logger, Exception exception, Guid messageId, string type, int attempts);

    private sealed record ClaimedMessage(Guid Id, string Type, string Payload, int Attempts);
}

/// <summary>Runs the <see cref="OutboxDispatcher"/> continuously in Platform.Worker.</summary>
public sealed partial class OutboxDispatcherService(
    OutboxDispatcher dispatcher, IOptions<OutboxOptions> options, ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                claimed = await dispatcher.DispatchOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDispatchFailed(logger, exception);
            }

            if (claimed == 0)
            {
                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatch failed; retrying after the poll interval.")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);
}
