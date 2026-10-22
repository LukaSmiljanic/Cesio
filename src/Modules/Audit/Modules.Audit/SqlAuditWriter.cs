using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Audit.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Audit;

/// <summary>
/// Appends to the single hash chain. The chain is serialised by an application lock owned by the transaction, so the
/// lock is held until the event commits or rolls back (audit.md §1).
/// </summary>
internal sealed class SqlAuditWriter(UnitOfWork unitOfWork, AuditDbContext audit, IRequestContext request, TimeProvider timeProvider)
    : IAuditWriter
{
    public const string ChainLock = "audit:chain";

    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await ApplicationLock.AcquireForTransactionAsync(unitOfWork.RequiredTransaction, ChainLock, LockTimeout, cancellationToken);

        var last = await audit.AuditEvents.AsNoTracking()
            .OrderByDescending(e => e.Sequence)
            .Select(e => new { e.Sequence, e.Hash })
            .FirstOrDefaultAsync(cancellationToken);
        var prevHash = last?.Hash ?? AuditChain.Genesis;

        var auditEvent = new AuditEvent
        {
            Id = SequentialGuid.NewGuid(),
            Sequence = (last?.Sequence ?? 0) + 1,
            OccurredAt = AuditChain.ToStoredPrecision(timeProvider.GetUtcNow()),
            ActorType = request.ActorType.ToString(),
            ActorUserId = request.UserId,
            ActorOrgId = request.OrganizationId,
            OnBehalfOfOrgId = entry.OnBehalfOfOrgId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Related = ToJson(entry.Related),
            PreviousState = entry.PreviousState,
            NewState = entry.NewState,
            Changes = entry.Changes is { Count: > 0 } changes ? JsonSerializer.Serialize(changes, JsonOptions.Web) : null,
            Reason = entry.Reason,
            CorrelationId = request.CorrelationId,
            IdempotencyKey = request.IdempotencyKey,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            DeviceId = request.DeviceId,
            MfaStepUpAt = request.MfaStepUpAt is { } stepUp ? AuditChain.ToStoredPrecision(stepUp) : null,
            EvidenceDocumentIds = ToJson(entry.EvidenceDocumentIds),
            PrevHash = prevHash,
        };
        auditEvent.Hash = AuditChain.ComputeHash(prevHash, auditEvent);

        audit.AuditEvents.Add(auditEvent);
        await audit.SaveChangesAsync(cancellationToken);
    }

    private static string? ToJson<T>(IReadOnlyList<T>? values) =>
        values is { Count: > 0 } ? JsonSerializer.Serialize(values, JsonOptions.Web) : null;
}

/// <summary>Appends read accesses to <c>audit.access_log</c> in the current transaction.</summary>
internal sealed class SqlAccessLog(UnitOfWork unitOfWork, AuditDbContext audit, IRequestContext request, TimeProvider timeProvider)
    : IAccessLog
{
    public async Task RecordAsync(AccessLogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _ = unitOfWork.RequiredTransaction;
        audit.AccessLog.Add(new AccessLogRecord
        {
            Id = SequentialGuid.NewGuid(),
            OccurredAt = timeProvider.GetUtcNow(),
            ActorType = request.ActorType.ToString(),
            ActorUserId = request.UserId,
            ActorOrgId = request.OrganizationId,
            OnBehalfOfOrgId = entry.OnBehalfOfOrgId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            CorrelationId = request.CorrelationId,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
        });
        await audit.SaveChangesAsync(cancellationToken);
    }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
