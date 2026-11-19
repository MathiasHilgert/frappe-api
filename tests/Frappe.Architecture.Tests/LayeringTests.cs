using System.Reflection;
using NetArchTest.Rules;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The dependency rules of the hexagon, checked on the compiled assemblies. Project references already prevent most
/// violations; these tests also catch the ones a reference cannot express (a package leaking through a transitive
/// reference).
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Results = typeof(Frappe.Results.Error).Assembly;
    private static readonly Assembly UseCases = typeof(Frappe.UseCases.IUnitOfWork).Assembly;
    private static readonly Assembly Aggregates = typeof(Frappe.Aggregates.AggregateRoot).Assembly;

    /// <summary>Anything an inner layer must never know about.</summary>
    private static readonly string[] Infrastructure =
    [
        "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "NATS", "Quartz",
        "ZiggyCreatures.Caching", "StackExchange.Redis", "OpenTelemetry", "Frappe.Persistence",
        "Frappe.Messaging", "Frappe.Caching", "Frappe.Scheduling", "Frappe.Http", "Frappe.Observability",
    ];

    [Test]
    [Arguments("Frappe.Results")]
    [Arguments("Frappe.UseCases")]
    [Arguments("Frappe.Aggregates")]
    public async Task Inner_layers_know_nothing_about_infrastructure(string assemblyName)
    {
        // Arrange
        var assembly = new[] { Results, UseCases, Aggregates }.Single(a => a.GetName().Name == assemblyName);

        // Act
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(Infrastructure).GetResult();

        // Assert
        await Assert.That(result.IsSuccessful).IsTrue().Because($"{assemblyName} depends on infrastructure: {string.Join(", ", result.FailingTypeNames ?? [])}");
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
}
