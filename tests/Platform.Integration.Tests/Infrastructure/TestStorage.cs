[assembly: AssemblyFixture(typeof(Platform.Integration.Tests.Infrastructure.TestStorage))]

namespace Platform.Integration.Tests.Infrastructure;

/// <summary>A temporary folder as the file storage of one test run (ADR-023: documents and audit digests on disk).</summary>
public sealed class TestStorage : IAsyncLifetime
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "cesio-tests", Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            // Audit digests are written read-only.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
