using Azure.Identity;
using Azure.Storage.Blobs;

namespace Shared.Infrastructure;

/// <summary>
/// A blob container from a connection string: locally an Azurite connection string; in Azure the account's blob endpoint
/// (<c>https://…</c>), reached with the managed identity (task 024), so no account key exists in configuration.
/// </summary>
public static class BlobContainers
{
    // One credential for the process: it caches its tokens (AZURE_CLIENT_ID selects the app's managed identity).
    private static readonly Lazy<DefaultAzureCredential> Credential = new(() => new DefaultAzureCredential());

    public static BlobContainerClient Create(string connectionString, string containerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return connectionString.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new BlobContainerClient(new Uri($"{connectionString.TrimEnd('/')}/{containerName}"), Credential.Value)
            : new BlobContainerClient(connectionString, containerName);
    }
}
