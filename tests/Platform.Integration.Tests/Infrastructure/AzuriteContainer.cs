using Testcontainers.Azurite;

[assembly: AssemblyFixture(typeof(Platform.Integration.Tests.Infrastructure.AzuriteContainer))]

namespace Platform.Integration.Tests.Infrastructure;

/// <summary>Azurite (local Azure Blob storage) for the document tests, one container per test run.</summary>
public sealed class AzuriteContainer : IAsyncLifetime
{
    private readonly Testcontainers.Azurite.AzuriteContainer _container =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithCommand("--skipApiVersionCheck")
            .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
