namespace Modules.Audit.Persistence;

/// <summary>One row of the hash chain (audit.md §2). Never updated or deleted.</summary>
internal sealed class AuditEvent
{
    public Guid Id { get; init; }

    public long Sequence { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public required string ActorType { get; init; }

    public Guid? ActorUserId { get; init; }

    public Guid? ActorOrgId { get; init; }

    public Guid? OnBehalfOfOrgId { get; init; }

    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public Guid EntityId { get; init; }

    public string? Related { get; init; }

    public string? PreviousState { get; init; }

    public string? NewState { get; init; }

    public string? Changes { get; init; }

    public string? Reason { get; init; }

    public required string CorrelationId { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }

    public string? DeviceId { get; init; }

    public DateTimeOffset? MfaStepUpAt { get; init; }

    public string? EvidenceDocumentIds { get; init; }

    public required string PrevHash { get; init; }

    public string Hash { get; set; } = string.Empty;
}

/// <summary>One read access (audit.md §4 <c>access_log</c>). Append-only, not hash-chained.</summary>
internal sealed class AccessLogRecord
{
    public Guid Id { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public required string ActorType { get; init; }

    public Guid? ActorUserId { get; init; }

    public Guid? ActorOrgId { get; init; }

    public Guid? OnBehalfOfOrgId { get; init; }

    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public Guid EntityId { get; init; }

    public required string CorrelationId { get; init; }

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }
}
