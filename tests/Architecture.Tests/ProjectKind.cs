namespace Architecture.Tests;

internal enum ProjectKind
{
    Host,
    Module,
    ModuleContracts,
    SharedKernel,
    SharedInfrastructure,
    Integration,
}

internal static class ProjectClassifier
{
    public static ProjectKind Classify(string project) => project switch
    {
        "Platform.Api" or "Platform.Worker" => ProjectKind.Host,
        "Shared.Kernel" => ProjectKind.SharedKernel,
        "Shared.Infrastructure" => ProjectKind.SharedInfrastructure,
        _ when project.StartsWith("Modules.", StringComparison.Ordinal) && project.EndsWith(".Contracts", StringComparison.Ordinal) => ProjectKind.ModuleContracts,
        _ when project.StartsWith("Modules.", StringComparison.Ordinal) => ProjectKind.Module,
        _ when project.StartsWith("Integrations.", StringComparison.Ordinal) => ProjectKind.Integration,
        _ => throw new InvalidOperationException($"Project '{project}' does not follow the naming in docs/architecture.md §3."),
    };

    /// <summary>Allowed project references (architecture.md §3, task 001).</summary>
    public static bool MayReference(string project, string reference) => (Classify(project), Classify(reference)) switch
    {
        (ProjectKind.Host, _) => true,
        (ProjectKind.Module, ProjectKind.SharedKernel or ProjectKind.SharedInfrastructure or ProjectKind.ModuleContracts or ProjectKind.Integration) => true,
        (ProjectKind.ModuleContracts, ProjectKind.SharedKernel or ProjectKind.ModuleContracts) => true,
        (ProjectKind.Integration, ProjectKind.SharedKernel or ProjectKind.SharedInfrastructure) => true,
        (ProjectKind.SharedInfrastructure, ProjectKind.SharedKernel) => true,
        _ => false,
    };
}
