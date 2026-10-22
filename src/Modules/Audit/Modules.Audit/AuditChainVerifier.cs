using Microsoft.EntityFrameworkCore;
using Modules.Audit.Persistence;

namespace Modules.Audit;

/// <summary>Result of a full chain check. <see cref="FirstInvalidSequence"/> is the first row whose sequence or hashes do not match.</summary>
public sealed record AuditChainVerification(bool IsValid, long EventCount, long LastSequence, string LastHash, long? FirstInvalidSequence);

/// <summary>Recomputes the whole hash chain (audit.md §1, task 003: chain verification detects a tampered row).</summary>
public sealed class AuditChainVerifier(AuditDbContext audit)
{
    private const int BatchSize = 1000;

    public async Task<AuditChainVerification> VerifyAsync(CancellationToken cancellationToken)
    {
        var count = 0L;
        var lastSequence = 0L;
        var lastHash = AuditChain.Genesis;
        while (true)
        {
            var after = lastSequence;
            var batch = await audit.AuditEvents.AsNoTracking()
                .Where(e => e.Sequence > after)
                .OrderBy(e => e.Sequence)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            foreach (var auditEvent in batch)
            {
                if (auditEvent.Sequence != lastSequence + 1
                    || auditEvent.PrevHash != lastHash
                    || auditEvent.Hash != AuditChain.ComputeHash(lastHash, auditEvent))
                {
                    return new AuditChainVerification(false, count, lastSequence, lastHash, auditEvent.Sequence);
                }

                count++;
                lastSequence = auditEvent.Sequence;
                lastHash = auditEvent.Hash;
            }

            if (batch.Count < BatchSize)
            {
                return new AuditChainVerification(true, count, lastSequence, lastHash, null);
            }
        }
    }
}
