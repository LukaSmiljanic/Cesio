using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Modules.Documents.Contracts;
using Shared.Infrastructure;

namespace Modules.Documents;

/// <summary>Private blob containers (security.md §4): uploads land in quarantine and move to clean after the scan.</summary>
public interface IDocumentStore
{
    Task PutQuarantinedAsync(string key, Stream content, CancellationToken cancellationToken);

    Task ReleaseAsync(string key, CancellationToken cancellationToken);

    Task<Stream> OpenCleanAsync(string key, CancellationToken cancellationToken);

    /// <summary>The quarantined blobs created before <paramref name="before"/> (task 020 orphan cleanup).</summary>
    IAsyncEnumerable<string> QuarantinedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken);

    Task DeleteQuarantinedAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Scans a file before it can be downloaded (task 006). Returns Clean, Infected or Pending (no verdict yet).</summary>
public interface IMalwareScanner
{
    Task<ScanStatus> ScanAsync(Stream content, CancellationToken cancellationToken);
}

internal sealed class BlobDocumentStore(IConfiguration configuration) : IDocumentStore
{
    public const string ConnectionStringName = "Documents";

    public const string QuarantineContainer = "quarantine";

    public const string CleanContainer = "clean";

    public async Task PutQuarantinedAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(QuarantineContainer, cancellationToken);
        await container.UploadBlobAsync(key, content, cancellationToken);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        var source = (await ContainerAsync(QuarantineContainer, cancellationToken)).GetBlobClient(key);
        var target = (await ContainerAsync(CleanContainer, cancellationToken)).GetBlobClient(key);
        await using (var stream = await source.OpenReadAsync(cancellationToken: cancellationToken))
        {
            await target.UploadAsync(stream, cancellationToken);
        }

        await source.DeleteAsync(cancellationToken: cancellationToken);
    }

    public async Task<Stream> OpenCleanAsync(string key, CancellationToken cancellationToken) =>
        await (await ContainerAsync(CleanContainer, cancellationToken)).GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken);

    public async IAsyncEnumerable<string> QuarantinedBeforeAsync(DateTimeOffset before, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = await ContainerAsync(QuarantineContainer, cancellationToken);
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            if (blob.Properties.CreatedOn is { } created && created < before)
            {
                yield return blob.Name;
            }
        }
    }

    public async Task DeleteQuarantinedAsync(string key, CancellationToken cancellationToken) =>
        await (await ContainerAsync(QuarantineContainer, cancellationToken)).DeleteBlobIfExistsAsync(key, cancellationToken: cancellationToken);

    /// <summary>Containers are created private (no public access) on first use.</summary>
    private async Task<BlobContainerClient> ContainerAsync(string name, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        var container = BlobContainers.Create(connectionString, name);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        return container;
    }
}

/// <summary>
/// Local and test scanner (configuration <c>Documents:Scanner = Fake</c>): a file containing the EICAR test string is
/// infected. Production uses Microsoft Defender
/// for Storage scan results (task 024).
/// </summary>
internal sealed class FakeMalwareScanner : IMalwareScanner
{
    private static readonly byte[] Eicar = Encoding.ASCII.GetBytes("EICAR-STANDARD-ANTIVIRUS-TEST-FILE");

    public async Task<ScanStatus> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.GetBuffer().AsSpan(0, (int)buffer.Length).IndexOf(Eicar) >= 0 ? ScanStatus.Infected : ScanStatus.Clean;
    }
}

/// <summary>
/// The scanner outside local development until Defender for Storage is wired in (task 024): it gives no verdict, so
/// every upload stays Pending in quarantine and cannot be downloaded (fail closed).
/// </summary>
internal sealed class DeferredMalwareScanner : IMalwareScanner
{
    public Task<ScanStatus> ScanAsync(Stream content, CancellationToken cancellationToken) => Task.FromResult(ScanStatus.Pending);
}
