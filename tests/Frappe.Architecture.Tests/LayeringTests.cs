using System.Reflection;
using NetArchTest.Rules;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The dependency rules of the hexagon, checked on the compiled assemblies. Project references already prevent most
/// violations; these tests also catch the ones a reference cannot express (a package leaking through a transitive
/// reference, telemetry code inside a module).
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Results = typeof(Frappe.Results.Error).Assembly;
    private static readonly Assembly Localization = typeof(Frappe.Localization.LanguageTag).Assembly;
    private static readonly Assembly Aggregates = typeof(Frappe.Aggregates.AggregateRoot).Assembly;
    private static readonly Assembly IntegrationEvents = typeof(Frappe.IntegrationEvents.IIntegrationEvent).Assembly;
    private static readonly Assembly Persistence = typeof(Frappe.Persistence.ModuleDatabase).Assembly;

    /// <summary>Anything a domain or application layer must never know about.</summary>
    private static readonly string[] Infrastructure =
    [
        "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "NATS", "Quartz",
        "ZiggyCreatures.Caching", "StackExchange.Redis", "OpenTelemetry", "Frappe.Persistence",
        "Frappe.Messaging", "Frappe.Caching", "Frappe.Scheduling", "Frappe.Http", "Frappe.Observability",
    ];

    private static async Task AssertAsync(NetArchTest.Rules.TestResult result, string rule)
    {
        var offenders = result.FailingTypeNames ?? [];
        await Assert.That(result.IsSuccessful).IsTrue().Because($"{rule}: {string.Join(", ", offenders)}");
    }

    [Test]
    [Arguments("Frappe.Results")]
    [Arguments("Frappe.Localization")]
    [Arguments("Frappe.Aggregates")]
    [Arguments("Frappe.IntegrationEvents")]
    public async Task Inner_layers_know_nothing_about_infrastructure(string assemblyName)
    {
        var assembly = new[] { Results, Localization, Aggregates, IntegrationEvents }
            .Single(a => a.GetName().Name == assemblyName);

        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(Infrastructure).GetResult();

        await AssertAsync(result, $"{assemblyName} depends on infrastructure");
    }

    [Test]
    public async Task The_rules_can_see_real_dependencies()
    {
        // Positive control: a rule that can never fail would prove nothing.
        var persistence = Types.InAssembly(Persistence).That().HaveDependencyOn("Microsoft.EntityFrameworkCore").GetTypes();

        await Assert.That(persistence.Any()).IsTrue();
    }
}
