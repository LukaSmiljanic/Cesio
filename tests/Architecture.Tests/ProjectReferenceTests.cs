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
}
