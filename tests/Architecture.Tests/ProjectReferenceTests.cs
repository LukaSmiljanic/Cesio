namespace Architecture.Tests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void Every_project_follows_the_naming_convention()
    {
        foreach (var project in SolutionProjects.Names)
        {
            _ = ProjectClassifier.Classify(project);
        }
    }

    [Fact]
    public void Projects_reference_only_allowed_projects()
    {
        var violations = SolutionProjects.References
            .SelectMany(entry => entry.Value
                .Where(reference => !ProjectClassifier.MayReference(entry.Key, reference))
                .Select(reference => $"{entry.Key} -> {reference}"))
            .ToList();

        Assert.True(violations.Count == 0, "Forbidden project references:\n" + string.Join('\n', violations));
    }

    /// <summary>The migrator creates every schema the API uses: a module the API hosts but the migrator misses has no tables after a deploy.</summary>
    [Fact]
    public void The_migrator_references_every_module_the_api_hosts()
    {
        var missing = SolutionProjects.References["Platform.Api"]
            .Where(p => p.StartsWith("Modules.", StringComparison.Ordinal) && !p.EndsWith(".Contracts", StringComparison.Ordinal))
            .Except(SolutionProjects.References["Platform.Migrator"], StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, "Modules the migrator does not migrate: " + string.Join(", ", missing));
    }
}
