using Frappe.Modularity;
using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>A module's Infrastructure is internal by default: other assemblies see only its <see cref="IModule"/>.</summary>
public sealed class VisibilityTests
{
    // Wolverine builds handler dependencies inline in the generated code only when their concrete type is public;
    // these adapters stay public so the static handlers need no service location.
    private static readonly string[] InlinedByWolverine = ["IdentityDbContext", "PrincipalRepository", "OwnerReads"];

    public static TheoryData<string> InfrastructureProjects =>
        [.. Solution.OfKind(ProjectKind.ModuleInfrastructure).Select(p => p.Name)];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(InfrastructureProjects))]
    public void Infrastructure_ExposesOnlyItsModule(string name)
    {
        var assembly = Solution.LoadAssembly(Solution.Projects.Single(p => p.Name == name));

        var exposed = assembly.GetExportedTypes()
            .Where(t => !t.IsNested && !typeof(IModule).IsAssignableFrom(t) && !InlinedByWolverine.Contains(t.Name))
            .Where(t => t.Namespace?.Contains(".Migrations", StringComparison.Ordinal) != true)
            .Select(t => t.FullName);

        exposed.ShouldBeEmpty();
    }
}
