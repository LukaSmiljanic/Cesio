namespace Modules.Audit.Contracts;

/// <summary>
/// Writes a business audit event inside the caller's unit-of-work transaction (audit.md §1, ADR-017). If the write
/// fails, the business change fails with it. The actor, request and chain fields are filled in by the writer.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}

/// <summary>Appends a read to <c>audit.access_log</c> (listing views, document downloads); same append-only rules, no hash chain.</summary>
public interface IAccessLog
{
    Task RecordAsync(AccessLogEntry entry, CancellationToken cancellationToken);
}

/// <summary>
/// One business action (audit.md §2). <paramref name="Action"/> is a named intent such as <c>Offer.Accepted</c>.
/// <paramref name="Changes"/> is a field-level diff of configured fields; sensitive values must already be masked.
/// </summary>
public sealed record AuditEntry(
    string Action,
    string EntityType,
    Guid EntityId,
    Guid? OnBehalfOfOrgId = null,
    string? PreviousState = null,
    string? NewState = null,
    IReadOnlyDictionary<string, string?>? Changes = null,
    string? Reason = null,
    IReadOnlyList<AuditReference>? Related = null,
    IReadOnlyList<Guid>? EvidenceDocumentIds = null);

/// <summary>A related entity of an audit event, e.g. the deal of an accepted offer.</summary>
public sealed record AuditReference(string Type, Guid Id);

/// <summary>One read access by the current actor.</summary>
public sealed record AccessLogEntry(string Action, string EntityType, Guid EntityId, Guid? OnBehalfOfOrgId = null);
