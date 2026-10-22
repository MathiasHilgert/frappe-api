using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>
/// Checks the csproj graph itself: type-level scanning misses references nobody uses yet, and those are exactly
/// the ones that later turn into accidental coupling.
/// </summary>
public sealed class ProjectReferenceTests
{
    public static TheoryData<string> Projects => [.. Solution.Projects.Select(p => p.Name)];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Projects))]
    public void Project_ReferencesOnlyAllowedProjects(string name)
    {
        var project = Solution.Projects.Single(p => p.Name == name);

        var forbidden = ForbiddenReferences(project, Solution.Projects);

        forbidden.ShouldBeEmpty($"{name} ({project.Kind}) must not reference: {string.Join(", ", forbidden)}");
    }

    [Fact]
    public void EveryModuleProject_IsLoadedByTheArchitectureTests()
    {
        var missing = UnloadableProjects(Solution.OfKind(ProjectKind.ModuleDomain, ProjectKind.ModuleApplication,
            ProjectKind.ModuleContracts, ProjectKind.ModuleInfrastructure));

        missing.ShouldBeEmpty("Reference the module's Infrastructure from the host so its assemblies are scanned.");
    }

    internal static List<string> ForbiddenReferences(Project project, IReadOnlyList<Project> projects) =>
        project.References
            .Select(reference => projects.SingleOrDefault(p => p.Name == reference)
                ?? throw new ShouldAssertException(
                    $"{project.Name} references '{reference}', which is not a project under src/."))
            .Where(target => !IsAllowed(project, target))
            .Select(target => target.Name)
            .ToList();

    /// <summary>Projects whose assembly is missing from the test output or present but not loadable.</summary>
    internal static List<string> UnloadableProjects(IEnumerable<Project> projects) =>
        projects.Where(p => !CanLoad(p)).Select(p => p.Name).ToList();

    private static bool CanLoad(Project project)
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, $"{project.Name}.dll")))
            return false;

        try
        {
            Solution.LoadAssembly(project);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return false;
        }
    }

    private static bool IsAllowed(Project from, Project to) => from.Kind switch
    {
        ProjectKind.SharedKernel or ProjectKind.ContractsAbstractions => false,
        ProjectKind.ApplicationAbstractions => to.Kind is ProjectKind.ContractsAbstractions,
        ProjectKind.Modularity => false,
        ProjectKind.Platform => to.Kind is ProjectKind.SharedKernel or ProjectKind.ContractsAbstractions
            or ProjectKind.ApplicationAbstractions or ProjectKind.Modularity or ProjectKind.Platform,
        ProjectKind.ModuleDomain => to.Kind is ProjectKind.SharedKernel,
        ProjectKind.ModuleContracts => to.Kind is ProjectKind.SharedKernel or ProjectKind.ContractsAbstractions,
        ProjectKind.ModuleApplication => to.Kind is ProjectKind.SharedKernel or ProjectKind.ContractsAbstractions
                or ProjectKind.ApplicationAbstractions or ProjectKind.ModuleContracts
            || (to.Kind is ProjectKind.ModuleDomain && to.Module == from.Module),
        ProjectKind.ModuleInfrastructure => to.Kind is ProjectKind.SharedKernel or ProjectKind.ContractsAbstractions
                or ProjectKind.ApplicationAbstractions or ProjectKind.Modularity or ProjectKind.Platform
                or ProjectKind.ModuleContracts
            || (to.Kind is ProjectKind.ModuleDomain or ProjectKind.ModuleApplication && to.Module == from.Module),
        ProjectKind.Host => true,
        _ => false,
    };
}
