using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Shared.Infrastructure.Persistence.Platform;

/// <summary>Versioned business parameters from <c>platform.business_parameters</c> (architecture.md §5).</summary>
public interface IBusinessParameters
{
    /// <summary>The value effective now, or <paramref name="defaultValue"/> when the parameter was never set.</summary>
    Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken);
}

internal sealed class SqlBusinessParameters(PlatformDbContext platform, TimeProvider timeProvider) : IBusinessParameters
{
    public async Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var value = await platform.BusinessParameters.AsNoTracking()
            .Where(p => p.ParamKey == key && p.EffectiveFrom <= now)
            .OrderByDescending(p => p.Version)
            .Select(p => p.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return value is null ? defaultValue : JsonSerializer.Deserialize<T>(value)!;
    }
}
