namespace Shared.Infrastructure.Persistence.Platform;

/// <summary>An integration event waiting for delivery (database.md §4: at-least-once, consumers idempotent by event ID).</summary>
internal sealed class OutboxMessage
{
    public Guid Id { get; init; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Earliest time of the next delivery attempt (backoff and claim lease).</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Set after the maximum number of attempts; the message is then only retried by hand.</summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

/// <summary>Marks that a consumer has handled an event, so redelivery does not repeat its side effects.</summary>
internal sealed class ProcessedEvent
{
    public Guid EventId { get; init; }

    public required string Consumer { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}

/// <summary>A command's idempotency key with the stored response for replay (database.md §4).</summary>
internal sealed class IdempotencyRecord
{
    public const int MaxKeyLength = 200;

    public required string IdemKey { get; init; }

    public Guid OrgId { get; init; }

    public Guid UserId { get; init; }

    public required string Endpoint { get; init; }

    public required string RequestSha256 { get; init; }

    public int? ResponseStatus { get; set; }

    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>A versioned business parameter (architecture.md §5: business parameters live in the DB).</summary>
internal sealed class BusinessParameter
{
    public required string ParamKey { get; init; }

    public int Version { get; init; }

    public required string Value { get; init; }

    public DateTimeOffset EffectiveFrom { get; init; }

    public Guid CreatedBy { get; init; }
}
