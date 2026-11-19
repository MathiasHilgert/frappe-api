using NetArchTest.Rules;
using Assembly = System.Reflection.Assembly;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The dependency rules of the hexagon, checked on the compiled assemblies. Project references already prevent most
/// violations; these tests also catch the ones a reference cannot express (a package leaking through a transitive
/// reference). The platform rows are fixed and the module rows come from one table, so a repository without modules
/// still runs every rule.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Persistence = typeof(Frappe.Persistence.ModuleDatabase).Assembly;
    private static readonly Assembly Modules = typeof(Frappe.Modules.IModule).Assembly;

    /// <summary>The platform assemblies whose inner-layer rules never change: no framework, database or broker.</summary>
    private static readonly string[] PlatformInnerLayers = ["Frappe.Results", "Frappe.UseCases", "Frappe.Aggregates"];

    /// <summary>
    /// The one table of business modules, by name (<c>Orders</c> for <c>Frappe.Orders.*</c>). Adding a module is one row
    /// here, plus a ProjectReference to its Domain, Application and Contracts projects in
    /// Frappe.Architecture.Tests.csproj: every rule below that walks modules loads its layers by naming convention.
    /// Empty while the repository has none.
    /// </summary>
    private static readonly string[] ModuleNames = [];

    /// <summary>The layers of a module that stay free of infrastructure.</summary>
    private static readonly string[] ModuleInnerLayers = ["Domain", "Application", "Contracts"];

    /// <summary>Anything an inner layer must never know about.</summary>
    private static readonly string[] Infrastructure =
    [
        "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "NATS", "Quartz",
        "ZiggyCreatures.Caching", "StackExchange.Redis", "OpenTelemetry", "Frappe.Persistence",
        "Frappe.Messaging", "Frappe.Caching", "Frappe.Scheduling", "Frappe.Http", "Frappe.Modules", "Frappe.Observability",
    ];

    /// <summary>Every inner-layer assembly name: the platform rows plus <c>Frappe.&lt;Module&gt;.&lt;Layer&gt;</c> for each module of the table.</summary>
    public static IEnumerable<string> InnerLayers() =>
        PlatformInnerLayers.Concat(ModuleNames.SelectMany(module => ModuleInnerLayers.Select(layer => $"Frappe.{module}.{layer}")));

    private static HashSet<string> ReferencedClosure(Assembly assembly)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Assembly>([assembly]);
        while (pending.TryPop(out var current))
        {
            foreach (var reference in current.GetReferencedAssemblies().Where(reference => seen.Add(reference.Name!)))
            {
                pending.Push(Assembly.Load(reference));
            }
        }

        return seen;
    }

    [Test]
    [MethodDataSource(nameof(InnerLayers))]
    public async Task Inner_layers_know_nothing_about_infrastructure(string assemblyName)
    {
        // Arrange
        var assembly = Assembly.Load(assemblyName);

        // Act
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(Infrastructure).GetResult();

        // Assert
        await Assert.That(result.IsSuccessful).IsTrue().Because($"{assemblyName} depends on infrastructure: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Test]
    public async Task The_infrastructure_rule_detects_an_assembly_that_uses_infrastructure()
    {
        // Act
        var result = Types.InAssembly(Persistence).ShouldNot().HaveDependencyOnAny(Infrastructure).GetResult();

        // Assert
        await Assert.That(result.IsSuccessful).IsFalse().Because("Frappe.Persistence uses Npgsql: a rule that passes on it checks nothing");
    }

    [Test]
    [Arguments("Frappe.Modules")]
    [Arguments("Frappe.Persistence")]
    public async Task Modules_and_persistence_never_reference_ASP_NET_Core(string assemblyName)
    {
        // Arrange
        var assembly = new[] { Modules, Persistence }.Single(a => a.GetName().Name == assemblyName);

        // Act
        var references = ReferencedClosure(assembly).Where(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)).ToList();
        var uses = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny("Microsoft.AspNetCore").GetResult();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(references).IsEmpty().Because($"{assemblyName} reaches ASP.NET Core through: {string.Join(", ", references)}");
            await Assert.That(uses.IsSuccessful).IsTrue().Because($"{assemblyName} uses ASP.NET Core: {string.Join(", ", uses.FailingTypeNames ?? [])}");
        }
    }

    [Test]
    public async Task The_reference_walk_reaches_the_packages_each_assembly_is_built_on()
    {
        // Act
        var persistence = ReferencedClosure(Persistence);
        var modules = ReferencedClosure(Modules);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(persistence).Contains("Npgsql").Because("the walk must see a transitive package for the ASP.NET Core rule to mean anything");
            await Assert.That(modules).Contains("Microsoft.Extensions.Hosting.Abstractions");
        }
    }

    [Test]
    public async Task Localization_depends_only_on_results()
    {
        // Arrange
        var assembly = typeof(Frappe.Localization.LanguageTag).Assembly;

        // Act
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(Infrastructure).GetResult();
        var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Frappe.", StringComparison.Ordinal)).ToArray();

        // Assert
        await Assert.That(result.IsSuccessful).IsTrue().Because($"Frappe.Localization depends on infrastructure: {string.Join(", ", result.FailingTypeNames ?? [])}");
        await Assert.That(references).IsEquivalentTo(["Frappe.Results"]);
    }

    [Test]
    public async Task Http_knows_no_storage_nor_messaging()
    {
        // Arrange
        var assembly = typeof(Frappe.Http.HttpRegistration).Assembly;
        string[] storage = ["Microsoft.EntityFrameworkCore", "Npgsql", "NATS", "Quartz", "Frappe.Persistence", "Frappe.Messaging", "Frappe.Scheduling", "Frappe.Observability"];

        // Act
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(storage).GetResult();
        var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Frappe.", StringComparison.Ordinal)).ToArray();

        // Assert
        await Assert.That(result.IsSuccessful).IsTrue().Because($"Frappe.Http depends on storage or messaging: {string.Join(", ", result.FailingTypeNames ?? [])}");
        await Assert.That(references).IsEquivalentTo(["Frappe.Localization", "Frappe.Results"]);
    }
}
