using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Modules.Audit.Persistence;

namespace Modules.Audit;

/// <summary>
/// The hash chain of audit.md §1: <c>hash = SHA-256(prev_hash || canonical_json(event))</c>. The canonical JSON has a
/// fixed field order and contains every column except the two hashes, so any change to a stored row breaks the chain.
/// </summary>
internal static class AuditChain
{
    /// <summary><c>prev_hash</c> of the first event.</summary>
    public static readonly string Genesis = new('0', 64);

    public static string ComputeHash(string prevHash, AuditEvent e)
    {
        var canonical = JsonSerializer.Serialize(new CanonicalEvent(
            e.Id, e.Sequence, Format(e.OccurredAt), e.ActorType, e.ActorUserId, e.ActorOrgId, e.OnBehalfOfOrgId, e.Action,
            e.EntityType, e.EntityId, e.Related, e.PreviousState, e.NewState, e.Changes, e.Reason, e.CorrelationId,
            e.IdempotencyKey, e.IpAddress, e.UserAgent, e.DeviceId, e.MfaStepUpAt is { } stepUp ? Format(stepUp) : null,
            e.EvidenceDocumentIds));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prevHash + canonical)));
    }

    /// <summary>Truncates to milliseconds in UTC, the precision of <c>datetimeoffset(3)</c>, so the hash survives a round trip.</summary>
    public static DateTimeOffset ToStoredPrecision(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private sealed record CanonicalEvent(
        Guid Id, long Sequence, string OccurredAt, string ActorType, Guid? ActorUserId, Guid? ActorOrgId, Guid? OnBehalfOfOrgId,
        string Action, string EntityType, Guid EntityId, string? Related, string? PreviousState, string? NewState, string? Changes,
        string? Reason, string CorrelationId, string? IdempotencyKey, string? IpAddress, string? UserAgent, string? DeviceId,
        string? MfaStepUpAt, string? EvidenceDocumentIds);
}
