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
    private static readonly Assembly MessagingStorage = typeof(Frappe.Messaging.Storage.Relay.RelayRoles).Assembly;
    private static readonly Assembly Observability = typeof(Frappe.Observability.FrappeTelemetry).Assembly;
    private static readonly Assembly Scheduling = typeof(Frappe.Scheduling.SchedulingRegistration).Assembly;
    private static readonly Assembly Worker = typeof(Frappe.Worker.WorkerComposition).Assembly;
    private static readonly Assembly NotificationsHttp = typeof(Frappe.Notifications.Http.NotificationsHttp).Assembly;
    private static readonly Assembly NotificationsInfrastructure = typeof(Frappe.Notifications.Infrastructure.NotificationsCoreServices).Assembly;
    private static readonly Assembly IdentityHttp = typeof(Frappe.Identity.Http.IdentityHttp).Assembly;
    private static readonly Assembly IdentityInfrastructure = typeof(Frappe.Identity.Infrastructure.IdentityCoreServices).Assembly;
    private static readonly Assembly AccessHttp = typeof(Frappe.Access.Http.AccessHttp).Assembly;
    private static readonly Assembly AccessInfrastructure = typeof(Frappe.Access.Infrastructure.AccessCoreServices).Assembly;

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
    [Arguments("Frappe.Notifications.Domain")]
    [Arguments("Frappe.Notifications.Application")]
    [Arguments("Frappe.Notifications.Contracts")]
    [Arguments("Frappe.Identity.Domain")]
    [Arguments("Frappe.Identity.Application")]
    [Arguments("Frappe.Identity.Contracts")]
    [Arguments("Frappe.Access.Application")]
    [Arguments("Frappe.Access.Contracts")]
    public async Task A_modules_inner_layers_know_nothing_about_infrastructure(string assemblyName)
    {
        // Arrange
        var assembly = System.Reflection.Assembly.Load(assemblyName);

        // Act
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(Infrastructure).GetResult();

        // Assert
        await AssertAsync(result, $"{assemblyName} depends on infrastructure");
    }

    [Test]
    [Arguments("Notifications")]
    [Arguments("Identity")]
    [Arguments("Access")]
    public async Task A_modules_Http_project_serves_use_cases_without_reaching_the_database_or_the_broker(string module)
    {
        // Arrange
        var http = module switch
        {
            "Identity" => IdentityHttp,
            "Access" => AccessHttp,
            _ => NotificationsHttp,
        };

        // Act
        var result = Types.InAssembly(http).ShouldNot().HaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore", "Npgsql", "NATS", "Quartz", "Frappe.Persistence", "Frappe.Messaging", "Frappe.Scheduling",
            $"Frappe.{module}.Infrastructure", $"Frappe.{module}.Domain").GetResult();

        // Assert
        await AssertAsync(result, $"Frappe.{module}.Http depends on storage, messaging, scheduling or the module's infrastructure");
    }

    [Test]
    [Arguments("Notifications")]
    [Arguments("Identity")]
    [Arguments("Access")]
    public async Task A_modules_Infrastructure_reaches_no_web_stack(string module)
    {
        // Arrange
        var infrastructure = module switch
        {
            "Identity" => IdentityInfrastructure,
            "Access" => AccessInfrastructure,
            _ => NotificationsInfrastructure,
        };

        // Act
        var result = Types.InAssembly(infrastructure).ShouldNot().HaveDependencyOnAny(
            "Microsoft.AspNetCore", "Frappe.Http", $"Frappe.{module}.Http").GetResult();

        // Assert
        await AssertAsync(result, $"Frappe.{module}.Infrastructure depends on the web stack");
    }

    [Test]
    public async Task The_Identity_producer_reaches_Identity_only_through_its_contracts_ARCH_05()
    {
        // Arrange
        var producer = typeof(Frappe.Notifications.Producers.Identity.IdentityProducerModule).Assembly;

        // Act
        var identityReferences = producer.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Frappe.Identity.", StringComparison.Ordinal))
            .ToList();

        // Assert
        await Assert.That(identityReferences).IsEquivalentTo(["Frappe.Identity.Contracts"]);
    }

    [Test]
    public async Task Notifications_core_and_its_plug_ins_other_than_a_producer_know_nothing_about_Identity()
    {
        // Arrange
        List<System.Reflection.Assembly> assemblies =
            [NotificationsHttp, NotificationsInfrastructure, typeof(Frappe.Notifications.Kinds.Account.AccountKindsModule).Assembly];
        for (var index = 0; index < assemblies.Count; index++)
        {
            foreach (var reference in assemblies[index].GetReferencedAssemblies())
            {
                if (reference.Name is { } name
                    && name.StartsWith("Frappe.Notifications.", StringComparison.Ordinal)
                    && !name.StartsWith("Frappe.Notifications.Producers.", StringComparison.Ordinal)
                    && assemblies.TrueForAll(known => known.GetName().Name != name))
                {
                    assemblies.Add(System.Reflection.Assembly.Load(reference));
                }
            }
        }

        // Act
        var identityReferences = assemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Frappe.Identity.", StringComparison.Ordinal))
            .ToList();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(assemblies.Select(assembly => assembly.GetName().Name))
                .Contains("Frappe.Notifications.Domain").And.Contains("Frappe.Notifications.Kinds.Account");
            await Assert.That(identityReferences).IsEmpty();
        }
    }

    [Test]
    [Arguments("Frappe.Identity")]
    [Arguments("Frappe.Identity.Domain")]
    [Arguments("Frappe.Identity.Application")]
    [Arguments("Frappe.Identity.Contracts")]
    [Arguments("Frappe.Identity.Infrastructure")]
    [Arguments("Frappe.Identity.Http")]
    public async Task Identity_knows_nothing_about_Notifications(string assemblyName)
    {
        // Arrange
        var assembly = System.Reflection.Assembly.Load(assemblyName);

        // Act
        var notificationsReferences = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Frappe.Notifications.", StringComparison.Ordinal))
            .ToList();

        // Assert
        await Assert.That(notificationsReferences).IsEmpty();
    }

    [Test]
    [Arguments("Frappe.Access.Application")]
    [Arguments("Frappe.Access.Contracts")]
    [Arguments("Frappe.Access.Infrastructure")]
    [Arguments("Frappe.Access.Http")]
    public async Task Access_references_no_other_module_S1(string assemblyName)
    {
        // Arrange
        var assembly = System.Reflection.Assembly.Load(assemblyName);

        // Act
        var moduleReferences = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Frappe.Identity.", StringComparison.Ordinal)
                || name.StartsWith("Frappe.Notifications.", StringComparison.Ordinal))
            .ToList();

        // Assert
        await Assert.That(moduleReferences).IsEmpty();
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
    public async Task Messaging_storage_maps_the_outbox_and_inbox_without_the_broker_the_web_stack_or_the_scheduler()
    {
        // Act
        var result = Types.InAssembly(MessagingStorage)
            .ShouldNot().HaveDependencyOnAny(
                "NATS", "Frappe.Messaging.Hosting", "Frappe.Messaging.JetStream", "Frappe.Scheduling", "Frappe.Http", "Frappe.Observability",
                "Microsoft.AspNetCore", "Quartz")
            .GetResult();

        // Assert
        await AssertAsync(result, "Messaging storage depends on the broker, the scheduler or the web stack");
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
    public async Task Scheduling_runs_jobs_without_knowing_messaging_or_the_web_stack()
    {
        // Act
        var result = Types.InAssembly(Scheduling)
            .ShouldNot().HaveDependencyOnAny("Frappe.Messaging", "Frappe.Http", "Frappe.Observability", "Microsoft.AspNetCore")
            .GetResult();

        // Assert
        await AssertAsync(result, "Scheduling depends on messaging, the web stack or telemetry code");
    }

    [Test]
    public async Task The_Worker_serves_no_http_and_maps_no_http_module()
    {
        // Arrange
        // A module's door loads its Http project in the Worker too (S1): what the Worker must never do is map it, so its own code names
        // no type of Frappe.Http (no IHttpModule, no MapHttpModules). WorkerCompositionTests checks the composed Worker has no endpoint.
        var http = Types.InAssembly(Worker).ShouldNot().HaveDependencyOn("Frappe.Http").GetResult();
        var web = Worker.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));

        // Assert
        using (Assert.Multiple())
        {
            await AssertAsync(http, "The Worker names an HTTP module or maps endpoints");
            await Assert.That(web).IsEmpty();
        }
    }

    [Test]
    public async Task The_rules_can_see_real_dependencies()
    {
        // Act
        // Positive control: a rule that can never fail would prove nothing.
        var caching = Types.InAssembly(Caching).That().HaveDependencyOn("ZiggyCreatures.Caching").GetTypes();
        var http = Types.InAssembly(Http).That().HaveDependencyOn("Microsoft.AspNetCore").GetTypes();
        var persistence = Types.InAssembly(Persistence).That().HaveDependencyOn("Microsoft.EntityFrameworkCore").GetTypes();
        var scheduling = Types.InAssembly(Scheduling).That().HaveDependencyOn("Quartz").GetTypes();
        var worker = Worker.GetReferencedAssemblies().Select(a => a.Name).Contains("Frappe.Scheduling");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(caching.Any()).IsTrue();
            await Assert.That(http.Any()).IsTrue();
            await Assert.That(persistence.Any()).IsTrue();
            await Assert.That(scheduling.Any()).IsTrue();
            await Assert.That(worker).IsTrue();
        }
    }
}
