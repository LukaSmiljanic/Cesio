using Microsoft.EntityFrameworkCore;
using Modules.Documents.Persistence;

namespace Modules.Documents;

/// <summary>
/// Daily (task 020, review follow-up): a quarantined blob older than a day without a document row belongs to an upload
/// whose transaction was rolled back; it is deleted. Blobs with a row (also Pending or Infected ones) are kept.
/// </summary>
internal sealed class OrphanBlobCleanup(DocumentsDbContext documents, IDocumentStore store, TimeProvider timeProvider)
{
    private const int ChunkSize = 500;

    private const int MaxDeletesPerRun = 500;

    public async Task<int> DeleteOrphansAsync(CancellationToken cancellationToken)
    {
        var deleted = 0;
        var chunk = new List<string>(ChunkSize);
        await foreach (var key in store.QuarantinedBeforeAsync(timeProvider.GetUtcNow().AddDays(-1), cancellationToken))
        {
            chunk.Add(key);
            if (chunk.Count == ChunkSize)
            {
                deleted += await DeleteUnknownAsync(chunk, MaxDeletesPerRun - deleted, cancellationToken);
                chunk.Clear();
                if (deleted >= MaxDeletesPerRun)
                {
                    return deleted;
                }
            }
        }

        return deleted + await DeleteUnknownAsync(chunk, MaxDeletesPerRun - deleted, cancellationToken);
    }

    private async Task<int> DeleteUnknownAsync(List<string> keys, int limit, CancellationToken cancellationToken)
    {
        var known = await documents.StoredDocuments.AsNoTracking().Where(d => keys.Contains(d.BlobKey)).Select(d => d.BlobKey).ToListAsync(cancellationToken);
        var orphans = keys.Except(known).Take(limit).ToList();
        foreach (var key in orphans)
        {
            await store.DeleteQuarantinedAsync(key, cancellationToken);
        }

        return orphans.Count;
    }
}
