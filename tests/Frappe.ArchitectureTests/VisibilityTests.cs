using System.Reflection;
using Frappe.Modularity;
using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>A module's Infrastructure is internal by default: other assemblies see only its <see cref="IModule"/>.</summary>
public sealed class VisibilityTests
{
    public static TheoryData<string> InfrastructureProjects =>
        [.. Solution.OfKind(ProjectKind.ModuleInfrastructure).Select(p => p.Name)];

    // Wolverine builds handler dependencies inline in the generated code only when their concrete type is public;
    // such adapters say so with [InlinedByWolverine], so the rule names no module's classes.
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(InfrastructureProjects))]
    public void Infrastructure_ExposesOnlyItsModule_AndTypesWolverineInlines(string name)
    {
        var assembly = Solution.LoadAssembly(Solution.Projects.Single(p => p.Name == name));

        var exposed = assembly.GetExportedTypes()
            .Where(t => !t.IsNested && !typeof(IModule).IsAssignableFrom(t) && t.GetCustomAttribute<InlinedByWolverineAttribute>() is null)
            .Where(t => t.Namespace?.Contains(".Migrations", StringComparison.Ordinal) != true)
            .Select(t => t.FullName);

        exposed.ShouldBeEmpty();
    }
}
