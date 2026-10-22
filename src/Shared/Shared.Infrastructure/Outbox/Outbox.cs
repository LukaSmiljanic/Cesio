using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Kernel;

namespace Shared.Infrastructure.Outbox;

/// <summary>Adds integration events to the outbox inside the current unit-of-work transaction.</summary>
public interface IOutbox
{
    Task AddAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}

/// <summary>Handles one integration event type for one named consumer. It runs in a transaction with its own writes.</summary>
public interface IIntegrationEventConsumer<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, Guid eventId, CancellationToken cancellationToken);
}

/// <summary>Outbox delivery settings (technical, not business parameters).</summary>
public sealed class OutboxOptions
{
    public int BatchSize { get; set; } = 20;

    /// <summary>After this many failed attempts a message is dead-lettered (logged and counted).</summary>
    public int MaxAttempts { get; set; } = 10;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a claimed batch is reserved for one dispatcher.</summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromHours(1);
}

public static class OutboxSetup
{
    internal static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddKernelConverters();

    /// <summary>Registers <typeparamref name="TConsumer"/> as consumer <paramref name="consumer"/> of <typeparamref name="TEvent"/>.</summary>
    public static IServiceCollection AddIntegrationEventConsumer<TEvent, TConsumer>(this IServiceCollection services, string consumer)
        where TEvent : IIntegrationEvent
        where TConsumer : class, IIntegrationEventConsumer<TEvent>
    {
        services.AddScoped<TConsumer>();
        services.AddSingleton(new OutboxConsumer(
            TEvent.EventType,
            consumer,
            (provider, payload, eventId, cancellationToken) => provider.GetRequiredService<TConsumer>().HandleAsync(
                JsonSerializer.Deserialize<TEvent>(payload, SerializerOptions)!, eventId, cancellationToken)));
        return services;
    }

    internal static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        services.AddOptions<OutboxOptions>().BindConfiguration("Outbox");
        services.AddScoped<IOutbox, SqlOutbox>();
        services.AddSingleton<OutboxDispatcher>();
        return services;
    }
}

/// <summary>One registered consumer of an event type (see <see cref="OutboxSetup.AddIntegrationEventConsumer{TEvent, TConsumer}"/>).</summary>
public sealed record OutboxConsumer(
    string EventType,
    string Consumer,
    Func<IServiceProvider, string, Guid, CancellationToken, Task> Handle);

internal sealed class SqlOutbox(UnitOfWork unitOfWork, PlatformDbContext platform, TimeProvider timeProvider) : IOutbox
{
    public async Task AddAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        _ = unitOfWork.RequiredTransaction;
        var now = timeProvider.GetUtcNow();
        platform.OutboxMessages.Add(new OutboxMessage
        {
            Id = SequentialGuid.NewGuid(),
            Type = TEvent.EventType,
            Payload = JsonSerializer.Serialize(integrationEvent, OutboxSetup.SerializerOptions),
            OccurredAt = now,
            NextAttemptAt = now,
        });
        await platform.SaveChangesAsync(cancellationToken);
    }
}
