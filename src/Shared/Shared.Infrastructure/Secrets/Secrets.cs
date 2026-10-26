using System.Collections.Concurrent;
using System.Text.Json;
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

    /// <summary>Removes the secret; nothing happens when it does not exist.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken);
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

    public async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await client.StartDeleteSecretAsync(name, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // Already gone.
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

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        _secrets.TryRemove(name, out _);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Local development only (<c>Secrets:LocalPersist = true</c>): test secrets in a file under the user's local application
/// data, so the API and the worker share them and they survive a restart. Never used in Azure (Key Vault).
/// </summary>
internal sealed class LocalFileSecretStore(string path) : ISecretStore
{
    private readonly Lock _file = new();

    public Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        lock (_file)
        {
            var secrets = Read();
            secrets[name] = value;
            Write(secrets);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string name, CancellationToken cancellationToken)
    {
        lock (_file)
        {
            return Task.FromResult(Read().TryGetValue(name, out var value) ? value : null);
        }
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        lock (_file)
        {
            var secrets = Read();
            if (secrets.Remove(name))
            {
                Write(secrets);
            }
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, string> Read() =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(StringComparer.Ordinal)
            : new(StringComparer.Ordinal);

    private void Write(Dictionary<string, string> secrets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(secrets));
    }
}

public static class SecretsSetup
{
    private static readonly string[] LocalEnvironments = ["Development", "local", "dev"];

    /// <summary>Key Vault when <c>KeyVault:Uri</c> is set; otherwise a local store (file or memory), which only local environments may use.</summary>
    public static IServiceCollection AddSecrets(this IServiceCollection services)
    {
        services.AddSingleton<ISecretStore>(provider =>
        {
            if (provider.GetRequiredService<IConfiguration>()["KeyVault:Uri"] is { Length: > 0 } uri)
            {
                return new KeyVaultSecretStore(new SecretClient(new Uri(uri), new DefaultAzureCredential()));
            }

            var environment = provider.GetRequiredService<IHostEnvironment>().EnvironmentName;
            if (!LocalEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"KeyVault:Uri must be configured in the '{environment}' environment.");
            }

            return provider.GetRequiredService<IConfiguration>().GetValue<bool>("Secrets:LocalPersist")
                ? new LocalFileSecretStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cesio", "dev", "secrets.json"))
                : new InMemorySecretStore();
        });
        return services;
    }
}
