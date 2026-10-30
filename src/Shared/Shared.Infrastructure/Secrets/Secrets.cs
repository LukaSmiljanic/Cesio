using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Persistence.Platform;

namespace Shared.Infrastructure.Secrets;

/// <summary>
/// Secrets such as SEF API keys (database.md §6, ADR-023): kept encrypted; the owning table keeps only the secret name.
/// </summary>
public interface ISecretStore
{
    Task SetAsync(string name, string value, CancellationToken cancellationToken);

    /// <summary>The secret, or null when it does not exist.</summary>
    Task<string?> GetAsync(string name, CancellationToken cancellationToken);

    /// <summary>Removes the secret; nothing happens when it does not exist.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}

/// <summary>
/// Secrets in <c>platform.secrets</c>, encrypted with ASP.NET Core data protection. The keys are in the data protection
/// key directory (encrypted with a certificate outside Development, <see cref="PlatformDefaults"/>), so a database backup
/// alone never reveals a secret. Inside a unit-of-work transaction the write joins it.
/// </summary>
internal sealed class DatabaseSecretStore(PlatformDbContext platform, IDataProtectionProvider dataProtection, TimeProvider timeProvider) : ISecretStore
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Cesio.Secrets.v1");

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        var ciphertext = _protector.Protect(value);
        if (await platform.Secrets.SingleOrDefaultAsync(s => s.Name == name, cancellationToken) is { } existing)
        {
            existing.Ciphertext = ciphertext;
            existing.UpdatedAt = timeProvider.GetUtcNow();
        }
        else
        {
            platform.Secrets.Add(new StoredSecret { Name = name, Ciphertext = ciphertext, UpdatedAt = timeProvider.GetUtcNow() });
        }

        await platform.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken) =>
        await platform.Secrets.AsNoTracking().Where(s => s.Name == name).Select(s => s.Ciphertext).SingleOrDefaultAsync(cancellationToken) is { } ciphertext
            ? _protector.Unprotect(ciphertext)
            : null;

    public async Task DeleteAsync(string name, CancellationToken cancellationToken) =>
        await platform.Secrets.Where(s => s.Name == name).ExecuteDeleteAsync(cancellationToken);
}

public static class SecretsSetup
{
    public static IServiceCollection AddSecrets(this IServiceCollection services)
    {
        services.AddScoped<ISecretStore, DatabaseSecretStore>();
        return services;
    }
}
