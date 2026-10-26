using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure;
using Shared.Infrastructure.Jobs;

namespace Modules.Audit;

/// <summary>
/// Daily: verifies the audit chain and writes a digest (last sequence and hash) to Blob storage (audit.md §1). The
/// container gets its immutable (WORM) retention policy in task 024; signing the digest is part of that task too.
/// </summary>
internal sealed partial class AuditDigestJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AuditDigestJob> logger)
    : SingletonJob(scopeFactory, configuration, logger)
{
    public const string ConnectionStringName = "AuditDigests";

    public const string ContainerName = "audit-digests";

    protected override string LockName => "audit-digest";

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var verification = await services.GetRequiredService<AuditChainVerifier>().VerifyAsync(cancellationToken);
        if (!verification.IsValid)
        {
            var jobLogger = services.GetRequiredService<ILogger<AuditDigestJob>>();
            LogChainBroken(jobLogger, verification.FirstInvalidSequence);
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var digest = JsonSerializer.Serialize(new
        {
            createdAt = now,
            verification.IsValid,
            verification.EventCount,
            verification.LastSequence,
            verification.LastHash,
            verification.FirstInvalidSequence,
        });

        var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        var container = BlobContainers.Create(connectionString, ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await container.UploadBlobAsync($"{now:yyyy-MM-dd'T'HHmmss'Z'}.json", BinaryData.FromString(digest), cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "The audit hash chain is broken at sequence {Sequence}.")]
    private static partial void LogChainBroken(ILogger logger, long? sequence);
}
