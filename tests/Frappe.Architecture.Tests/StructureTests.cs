using System.Xml.Linq;
using Frappe.Aggregates;
using Frappe.Testing;
using Frappe.UseCases;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The structure rules that can be read off the solution (STR-01, STR-04, STR-05, STR-07, STR-08, STR-10). Each test runs its rule on the real
/// code and on a violating fixture, so the rule is proven able to see a breach.
/// </summary>
public sealed class StructureTests
{
    private static readonly string[] Modules = ModuleDoorTests.AllModules;

    private static readonly string[] Projects = StructureRules.ModuleProjects();

    private static readonly System.Reflection.Assembly[] ProductionAssemblies =
    [
        .. ArchitectureRules.ProductionGraph.Keys.Select(name => System.Reflection.Assembly.Load(name)),
    ];

    private static readonly Type[] ModuleTypes =
    [
        .. ArchitectureRules.ModuleAssemblies(ArchitectureRules.ProductionGraph.Keys, Modules)
            .SelectMany(name => System.Reflection.Assembly.Load(name).GetTypes()),
    ];

    private static readonly Type[] AllTypes = [.. ProductionAssemblies.SelectMany(assembly => assembly.GetTypes())];

    private static string SrcModules(string path) => Path.Combine(RepositoryRoot.FullPath, "src", "Modules", path);

    [Test]
    public async Task Every_module_has_the_door_and_the_five_layers()
    {
        // Act
        var missing = StructureRules.MissingProjects(Projects, Modules);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(Modules).IsNotEmpty();
            await Assert.That(missing).IsEmpty().Because($"STR-01, a module lacks a project: {string.Join(", ", missing)}");
        }
    }

    [Test]
    public async Task The_project_set_rule_can_see_a_module_without_http()
    {
        // Act
        var missing = StructureRules.MissingProjects(
            [.. Projects.Where(path => !path.EndsWith("Frappe.Identity.Http.csproj", StringComparison.Ordinal))], Modules);

        // Assert
        await Assert.That(missing).IsEquivalentTo(["Identity/Frappe.Identity.Http/Frappe.Identity.Http.csproj"]);
    }

    [Test]
    public async Task Every_project_of_a_module_is_its_door_a_layer_or_a_named_plug_in()
    {
        // Act
        var offenders = StructureRules.PlugInOffenders(Projects, Modules);

        // Assert
        await Assert.That(offenders).IsEmpty().Because($"STR-04: {string.Join(", ", offenders)}");
    }

    [Test]
    public async Task The_plug_in_rule_can_see_a_misnamed_or_misplaced_plug_in()
    {
        // Arrange
        string[] fixture =
        [
            "Notifications/Extensions/Frappe.Notifications.Identity/Frappe.Notifications.Identity.csproj",
            "Notifications/Channels/Frappe.Notifications.Channels.Email/Frappe.Notifications.Channels.Email.csproj",
            "Notifications/Extensions/Frappe.Notifications.Senders.Email/Frappe.Notifications.Senders.Email.csproj",
        ];

        // Act
        var offenders = StructureRules.PlugInOffenders(fixture, Modules);

        // Assert
        await Assert.That(offenders).IsEquivalentTo(fixture[..2]);
    }

    [Test]
    public async Task Every_handler_is_named_for_its_request()
    {
        // Act
        var offenders = StructureRules.MisnamedHandlers(ModuleTypes);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(ModuleTypes.Count(type => type.Name.EndsWith("Handler", StringComparison.Ordinal))).IsGreaterThan(0);
            await Assert.That(offenders).IsEmpty().Because($"STR-05: {string.Join(", ", offenders)}");
        }
    }

    [Test]
    public async Task The_handler_rule_can_see_a_misnamed_handler()
    {
        // Act
        var offenders = StructureRules.MisnamedHandlers([typeof(MisnamedHandler), typeof(GetThingHandler)]);

        // Assert
        await Assert.That(offenders).IsEquivalentTo([typeof(MisnamedHandler).FullName!]);
    }

    [Test]
    public async Task Only_domain_types_extend_AggregateRoot()
    {
        // Act
        var offenders = StructureRules.AggregatesOutsideDomain(AllTypes);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(AllTypes.Count(type => type.IsSubclassOf(typeof(AggregateRoot)))).IsGreaterThan(0);
            await Assert.That(offenders).IsEmpty().Because($"STR-07: {string.Join(", ", offenders)}");
        }
    }

    [Test]
    public async Task The_aggregate_rule_can_see_an_aggregate_outside_domain()
    {
        // Act
        var offenders = StructureRules.AggregatesOutsideDomain([typeof(SessionRow), typeof(string)]);

        // Assert
        await Assert.That(offenders).IsEquivalentTo([typeof(SessionRow).FullName!]);
    }

    [Test]
    public async Task No_Row_type_extends_AggregateRoot()
    {
        // Act
        var offenders = StructureRules.RowsThatAreAggregates(AllTypes);

        // Assert
        await Assert.That(offenders).IsEmpty().Because($"STR-08: {string.Join(", ", offenders)}");
    }

    [Test]
    public async Task The_row_rule_can_see_a_row_that_is_an_aggregate()
    {
        // Act
        var offenders = StructureRules.RowsThatAreAggregates([typeof(SessionRow), typeof(PlainRow)]);

        // Assert
        await Assert.That(offenders).IsEquivalentTo([typeof(SessionRow).FullName!]);
    }

    [Test]
    public async Task InternalsVisibleTo_only_names_the_targets_STR_10_allows()
    {
        // Arrange
        var declarations = StructureRules.Declarations(Projects, path => XDocument.Load(SrcModules(path)));

        // Act
        var offenders = StructureRules.ForbiddenVisibility(declarations, Modules);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(declarations).IsNotEmpty();
            await Assert.That(offenders).IsEmpty().Because($"STR-10: {string.Join(", ", offenders)}");
        }
    }

    [Test]
    public async Task No_source_declares_InternalsVisibleTo_as_an_assembly_attribute()
    {
        // Arrange
        var sources = Directory.EnumerateFiles(Path.Combine(RepositoryRoot.FullPath, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        // Act
        var offenders = StructureRules.AttributeDeclarations(sources, File.ReadAllText);

        // Assert
        await Assert.That(offenders).IsEmpty().Because($"STR-10: {string.Join(", ", offenders)}");
    }

    [Test]
    public async Task The_visibility_rule_can_see_a_host_or_a_production_assembly_as_target()
    {
        // Act
        var offenders = StructureRules.ForbiddenVisibility(
            [
                "Frappe.Notifications.Infrastructure -> Frappe.Worker",
                "Frappe.Identity.Application -> Frappe.Notifications.UnitTests",
                "Frappe.Identity.Infrastructure -> Frappe.Identity",
                "Frappe.Identity.Http -> Frappe.Api.IntegrationTests",
            ],
            Modules);

        // Assert
        await Assert.That(offenders).IsEquivalentTo(
            ["Frappe.Identity.Application -> Frappe.Notifications.UnitTests", "Frappe.Notifications.Infrastructure -> Frappe.Worker"]);
    }

    [Test]
    public async Task The_attribute_rule_can_see_an_assembly_attribute()
    {
        // Act
        var offenders = StructureRules.AttributeDeclarations(
            ["a.cs", "b.cs"], path => path == "a.cs" ? "[assembly: InternalsVisibleTo(\"X\")]" : "class B;");

        // Assert
        await Assert.That(offenders).IsEquivalentTo(["a.cs"]);
    }

    /// <summary>A use case, to name handlers after.</summary>
    internal sealed record GetThing : IQuery<int>;

    /// <summary>A handler named after its request.</summary>
    internal sealed class GetThingHandler : IQueryHandler<GetThing, int>
    {
        public Task<Frappe.Results.Result<int>> HandleAsync(GetThing query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>A handler whose name is not its request's.</summary>
    internal sealed class MisnamedHandler : IQueryHandler<GetThing, int>
    {
        public Task<Frappe.Results.Result<int>> HandleAsync(GetThing query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>An aggregate that is not in a Domain assembly and is named like a row.</summary>
    internal sealed class SessionRow : AggregateRoot;

    /// <summary>A row that is not an aggregate.</summary>
    internal sealed class PlainRow
    {
        public int Id { get; init; }
    }
}
