using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure;
using Shared.Infrastructure.Jobs;

namespace Modules.Audit;

/// <summary>
/// Daily: verifies the audit chain and writes a digest (last sequence and hash) as a read-only file under
/// <c>{Storage:Root}/audit-digests</c> (audit.md §1, ADR-023). The digests are copied off the server daily (runbooks.md);
/// that copy, not the file attribute, is what makes them hard to change.
/// </summary>
internal sealed partial class AuditDigestJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AuditDigestJob> logger)
    : SingletonJob(scopeFactory, configuration, logger)
{
    public const string Area = "audit-digests";

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

        var root = FileStorage.Root(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IHostEnvironment>());
        var path = FileStorage.PathFor(root, Area, $"{now:yyyy-MM-dd'T'HHmmss'Z'}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, digest, cancellationToken);
        File.SetAttributes(path, FileAttributes.ReadOnly);
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "The audit hash chain is broken at sequence {Sequence}.")]
    private static partial void LogChainBroken(ILogger logger, long? sequence);
}
