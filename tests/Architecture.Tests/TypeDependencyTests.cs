using System.Reflection;
using Assembly = System.Reflection.Assembly;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Architecture.Tests;

public sealed class TypeDependencyTests
{
    private static readonly Dictionary<string, Assembly> Assemblies = SolutionProjects.Names
        .ToDictionary(name => name, name => Assembly.Load(new AssemblyName(name)), StringComparer.Ordinal);

    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader()
        .LoadAssemblies([.. Assemblies.Values])
        .Build();

    public static TheoryData<string> ModuleProjects => [.. ProjectsOf(ProjectKind.Module)];

    public static TheoryData<string> ContractProjects => [.. ProjectsOf(ProjectKind.ModuleContracts)];

    [Theory]
    [MemberData(nameof(ModuleProjects))]
    public void Module_depends_on_other_modules_only_through_contracts(string module)
    {
        var otherModules = ProjectsOf(ProjectKind.Module).Where(name => name != module).ToArray();

        TypesOf(module).Should().NotDependOnAny(TypesIn(otherModules))
            .WithoutRequiringPositiveResults() // scaffolded projects may contain no types yet
            .Check(Architecture);
    }

    [Theory]
    [MemberData(nameof(ContractProjects))]
    public void Contracts_do_not_depend_on_implementations(string contracts)
    {
        var implementations = SolutionProjects.Names
            .Where(name => ProjectClassifier.Classify(name) is not (ProjectKind.ModuleContracts or ProjectKind.SharedKernel))
            .ToArray();

        TypesOf(contracts).Should().NotDependOnAny(TypesIn(implementations))
            .WithoutRequiringPositiveResults() // scaffolded projects may contain no types yet
            .Check(Architecture);
    }

    private static IEnumerable<string> ProjectsOf(ProjectKind kind) =>
        SolutionProjects.Names.Where(name => ProjectClassifier.Classify(name) == kind).Order(StringComparer.Ordinal);

    private static ArchUnitNET.Fluent.Syntax.Elements.Types.GivenTypesConjunction TypesOf(string project) =>
        Types().That().ResideInAssembly(Assemblies[project]);

    private static ArchUnitNET.Fluent.Syntax.Elements.Types.GivenTypesConjunction TypesIn(string[] projects) =>
        Types().That().ResideInAssembly(Assemblies[projects[0]], [.. projects.Skip(1).Select(name => Assemblies[name])]);
}
