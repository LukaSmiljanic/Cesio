using System.Collections.Concurrent;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Shared.Infrastructure.Secrets;

/// <summary>
/// Secrets that must not live in the database, such as SEF API keys (database.md §6): Azure Key Vault in Azure; the
/// database keeps only the secret name.
/// </summary>
public interface ISecretStore
{
    Task SetAsync(string name, string value, CancellationToken cancellationToken);

    /// <summary>The secret, or null when it does not exist.</summary>
    Task<string?> GetAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Key Vault through managed identity (<see cref="DefaultAzureCredential"/>), configured by <c>KeyVault:Uri</c>.</summary>
internal sealed class KeyVaultSecretStore(SecretClient client) : ISecretStore
{
    public async Task SetAsync(string name, string value, CancellationToken cancellationToken) =>
        await client.SetSecretAsync(name, value, cancellationToken);

    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return (await client.GetSecretAsync(name, cancellationToken: cancellationToken)).Value.Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }
}

/// <summary>Local development only: secrets in memory, lost when the process stops (re-enter the SEF key).</summary>
internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        _secrets[name] = value;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_secrets.TryGetValue(name, out var value) ? value : null);
}

public static class SecretsSetup
{
    private static readonly string[] LocalEnvironments = ["Development", "local", "dev"];

    /// <summary>Key Vault when <c>KeyVault:Uri</c> is set; otherwise the in-memory store, which only local environments may use.</summary>
    public static IServiceCollection AddSecrets(this IServiceCollection services)
    {
        services.AddSingleton<ISecretStore>(provider =>
        {
            if (provider.GetRequiredService<IConfiguration>()["KeyVault:Uri"] is { Length: > 0 } uri)
            {
                return new KeyVaultSecretStore(new SecretClient(new Uri(uri), new DefaultAzureCredential()));
            }

            var environment = provider.GetRequiredService<IHostEnvironment>().EnvironmentName;
            return LocalEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase)
                ? new InMemorySecretStore()
                : throw new InvalidOperationException($"KeyVault:Uri must be configured in the '{environment}' environment.");
        });
        return services;
    }
}
