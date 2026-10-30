using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Shared.Infrastructure;

/// <summary>
/// The server's file storage (ADR-023): documents and audit digests under <c>Storage:Root</c>, one folder per area.
/// Locally it defaults to the user's local application data; on a server it must be configured (a mounted volume
/// that is backed up).
/// </summary>
public static class FileStorage
{
    public static string Root(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        return configuration["Storage:Root"] is { Length: > 0 } root
            ? root
            : environment.IsDevelopment()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cesio", "dev", "files")
                : throw new InvalidOperationException("Storage:Root must be set outside Development.");
    }

    /// <summary>The full path of <paramref name="key"/> in <paramref name="area"/>; a key that would leave the area is refused.</summary>
    public static string PathFor(string root, string area, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var folder = Path.GetFullPath(Path.Combine(root, area));
        var path = Path.GetFullPath(Path.Combine(folder, key));
        if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("The storage key leaves its folder.", nameof(key));
        }

        return path;
    }
}
