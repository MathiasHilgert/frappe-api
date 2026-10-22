using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>The architecture checks must fail with a message that names the offending project.</summary>
public sealed class ProjectReferenceFailureTests
{
    [Fact]
    public void ForbiddenReferences_ToAnUnknownProject_FailsNamingIt()
    {
        var host = new Project("Frappe.Api", ProjectKind.Host, null, ["Frappe.Missing"]);

        var failure = Should.Throw<ShouldAssertException>(() => ProjectReferenceTests.ForbiddenReferences(host, [host]));

        failure.Message.ShouldContain("Frappe.Missing");
    }

    [Fact]
    public void UnloadableProjects_IncludesAnAssemblyThatExistsButCannotBeLoaded()
    {
        var name = $"Frappe.Corrupt{Guid.NewGuid():N}.Domain";
        var path = Path.Combine(AppContext.BaseDirectory, $"{name}.dll");
        File.WriteAllText(path, "not an assembly");

        try
        {
            var project = new Project(name, ProjectKind.ModuleDomain, "Corrupt", []);

            ProjectReferenceTests.UnloadableProjects([project]).ShouldBe([name]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
