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
    private static readonly Assembly Localization = typeof(Frappe.Localization.LanguageTag).Assembly;
    private static readonly Assembly Aggregates = typeof(Frappe.Aggregates.AggregateRoot).Assembly;
    private static readonly Assembly IntegrationEvents = typeof(Frappe.IntegrationEvents.IIntegrationEvent).Assembly;
    private static readonly Assembly UseCases = typeof(Frappe.UseCases.ICommand<>).Assembly;
    private static readonly Assembly Caching = typeof(Frappe.Caching.CachingOptions).Assembly;
    private static readonly Assembly Http = typeof(Frappe.Http.HttpRegistration).Assembly;
    private static readonly Assembly Persistence = typeof(Frappe.Persistence.ModuleDatabase).Assembly;
    private static readonly Assembly Messaging = typeof(Frappe.Messaging.MessagingMetrics).Assembly;
    private static readonly Assembly Observability = typeof(Frappe.Observability.FrappeTelemetry).Assembly;

    /// <summary>Anything an inner layer must never know about.</summary>
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
    [Arguments("Frappe.UseCases")]
    public async Task Inner_layers_know_nothing_about_infrastructure(string assemblyName)
    {
        // Arrange
        var assembly = new[] { Results, Localization, Aggregates, IntegrationEvents, UseCases }
            .Single(a => a.GetName().Name == assemblyName);

        // Act
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(Infrastructure).GetResult();

        // Assert
        await AssertAsync(result, $"{assemblyName} depends on infrastructure");
    }

    [Test]
    public async Task Http_serves_use_cases_without_reaching_the_databases_the_broker_or_the_scheduler()
    {
        // Act
        var result = Types.InAssembly(Http).ShouldNot().HaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore", "Npgsql", "NATS", "Quartz", "Frappe.Persistence", "Frappe.Messaging", "Frappe.Scheduling",
            "Frappe.Observability").GetResult();

        // Assert
        await AssertAsync(result, "Frappe.Http depends on storage, messaging or scheduling");
    }

    [Test]
    public async Task Persistence_knows_no_capability_built_on_it()
    {
        // Act
        // Messaging adds its outbox through the callback of AddModuleDatabase, so the dependency never points back.
        var result = Types.InAssembly(Persistence)
            .ShouldNot().HaveDependencyOnAny(
                "Frappe.Messaging", "Frappe.Scheduling", "Frappe.Http", "Frappe.Observability", "Microsoft.AspNetCore", "Quartz", "NATS")
            .GetResult();

        // Assert
        await AssertAsync(result, "Persistence depends on a capability built on it, on the web stack or on a broker");
    }

    [Test]
    public async Task Messaging_publishes_and_consumes_without_knowing_the_web_stack_or_the_scheduler()
    {
        // Act
        var result = Types.InAssembly(Messaging)
            .ShouldNot().HaveDependencyOnAny("Frappe.Scheduling", "Frappe.Http", "Frappe.Observability", "Microsoft.AspNetCore", "Quartz")
            .GetResult();

        // Assert
        await AssertAsync(result, "Messaging depends on the scheduler, the web stack or telemetry code");
    }

    [Test]
    public async Task Observability_decorates_handlers_without_knowing_storage_or_transport()
    {
        // Act
        var result = Types.InAssembly(Observability)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore", "Npgsql", "Frappe.Persistence", "Frappe.Messaging", "Frappe.Http",
                "Microsoft.AspNetCore", "NATS", "Quartz")
            .GetResult();

        // Assert
        await AssertAsync(result, "Observability depends on storage, on the web stack or on a broker");
    }

    [Test]
    public async Task The_rules_can_see_real_dependencies()
    {
        // Act
        // Positive control: a rule that can never fail would prove nothing.
        var caching = Types.InAssembly(Caching).That().HaveDependencyOn("ZiggyCreatures.Caching").GetTypes();
        var http = Types.InAssembly(Http).That().HaveDependencyOn("Microsoft.AspNetCore").GetTypes();
        var persistence = Types.InAssembly(Persistence).That().HaveDependencyOn("Microsoft.EntityFrameworkCore").GetTypes();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(caching.Any()).IsTrue();
            await Assert.That(http.Any()).IsTrue();
            await Assert.That(persistence.Any()).IsTrue();
        }
    }
}
