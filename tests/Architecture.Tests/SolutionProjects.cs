using System.Xml.Linq;

namespace Architecture.Tests;

/// <summary>The production projects under src/ and the projects each one references, read from the .csproj files.</summary>
internal static class SolutionProjects
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> References { get; } = Load();

    public static IEnumerable<string> Names => References.Keys;

    private static Dictionary<string, IReadOnlyList<string>> Load()
    {
        var src = Path.Combine(FindRepositoryRoot(), "src");
        return Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path),
                path => (IReadOnlyList<string>)XDocument.Load(path)
                    .Descendants("ProjectReference")
                    .Select(reference => Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!))
                    .ToList(),
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Faktoring.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Faktoring.slnx not found above the test output directory.");
    }
}
