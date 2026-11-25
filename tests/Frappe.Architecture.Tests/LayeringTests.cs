using System.Reflection;
using Frappe.Testing;
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

    private static string[] ModuleProjects(string layer) => [.. ModuleDoorTests.AllModules.Select(module => $"Frappe.{module}.{layer}")];

    private static Assembly[] ModuleAssembliesIn(string layer) => [.. ModuleProjects(layer).Select(name => System.Reflection.Assembly.Load(name))];

    private static readonly Assembly FixtureAssembly = typeof(LayeringTests).Assembly;

    private static PredicateList Scope(Assembly assembly) => Types.InAssembly(assembly).That().ResideInNamespaceStartingWith("Frappe");

    private static PredicateList Fixtures() => Types.InAssembly(FixtureAssembly).That().HaveName("RuleBreakers");

    [Test]
    public async Task The_http_adapter_reaches_use_cases_only_through_the_application_layer()
    {
        // Act
        var offenders = ModuleAssembliesIn("Http").SelectMany(http => HttpBreaks(Scope(http)).FailingTypeNames ?? []).ToArray();

        // Assert
        await Assert.That(offenders).IsEmpty()
            .Because($"an *.Http project reaches storage, caching, messaging or the module's inside: {string.Join(", ", offenders)}");
    }

    private static NetArchTest.Rules.TestResult HttpBreaks(PredicateList types) => types.ShouldNot().HaveDependencyOnAny(
        "Microsoft.EntityFrameworkCore", "Npgsql", "NATS", "Quartz", "StackExchange.Redis", "Frappe.Persistence", "Frappe.Caching",
        "Frappe.Messaging", "Frappe.Scheduling", "Frappe.Access.Infrastructure", "Frappe.Identity.Infrastructure",
        "Frappe.Notifications.Infrastructure", "Frappe.Access.Domain", "Frappe.Identity.Domain", "Frappe.Notifications.Domain").GetResult();

    [Test]
    public async Task Infrastructure_adapters_do_not_serve_http()
    {
        // Act
        var offenders = ModuleAssembliesIn("Infrastructure")
            .SelectMany(infrastructure => InfrastructureBreaks(Scope(infrastructure)).FailingTypeNames ?? [])
            .ToArray();

        // Assert
        await Assert.That(offenders).IsEmpty().Because($"an *.Infrastructure project reaches the web stack: {string.Join(", ", offenders)}");
    }

    private static NetArchTest.Rules.TestResult InfrastructureBreaks(PredicateList types) => types.ShouldNot().HaveDependencyOnAny(
        ["Microsoft.AspNetCore", "Frappe.Http", .. ModuleDoorTests.AllModules.Select(module => $"Frappe.{module}.Http")]).GetResult();

    [Test]
    public async Task The_http_and_infrastructure_rules_can_see_a_violation_ARCH_04()
    {
        // Act
        var web = InfrastructureBreaks(Fixtures());
        var http = HttpBreaks(Fixtures());
        var persistence = Types.InAssembly(typeof(Frappe.Persistence.ModuleDatabase).Assembly)
            .That().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetTypes();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(web.IsSuccessful).IsFalse();
            await Assert.That(http.IsSuccessful).IsFalse();
            await Assert.That(persistence.Any()).IsTrue();
            await Assert.That(ModuleAssembliesIn("Http").Length).IsEqualTo(ModuleDoorTests.AllModules.Length);
        }
    }

    [Test]
    public async Task The_domain_does_not_depend_on_its_use_cases()
    {
        // Act
        var offenders = ModuleDoorTests.AllModules
            .SelectMany(module => DomainBreaks(Scope(System.Reflection.Assembly.Load($"Frappe.{module}.Domain")), module).FailingTypeNames ?? [])
            .ToArray();
        var seen = DomainBreaks(Fixtures(), "Identity");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(offenders).IsEmpty()
                .Because($"a Domain names its Application project or Frappe.UseCases: {string.Join(", ", offenders)}");
            await Assert.That(seen.IsSuccessful).IsFalse().Because("the rule must fail on a type that names Frappe.UseCases");
        }
    }

    private static NetArchTest.Rules.TestResult DomainBreaks(PredicateList types, string module) =>
        types.ShouldNot().HaveDependencyOnAny($"Frappe.{module}.Application", "Frappe.UseCases").GetResult();

    [Test]
    public async Task A_module_reaches_another_only_through_an_allow_listed_contracts_reference_ARCH_05()
    {
        // Arrange
        var graph = ArchitectureRules.ProductionGraph;

        // Act
        var offenders = ArchitectureRules.CrossModuleOffenders(graph, ModuleDoorTests.AllModules, CrossModuleAllowList);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(offenders).IsEmpty().Because($"a module reaches another past the allow-list: {string.Join(", ", offenders)}");
            await Assert.That(CrossModuleAllowList.All(entry => graph[entry.Assembly].Contains($"Frappe.{entry.Module}.Contracts")))
                .IsTrue().Because("an allow-list entry no assembly uses any more must be removed");
        }
    }

    [Test]
    public async Task The_isolation_rule_can_see_a_module_reaching_into_another_ARCH_05()
    {
        // Arrange
        string[] modules = ["Identity", "Notifications"];
        var graph = new Dictionary<string, string[]>
        {
            ["Frappe.Notifications.Application"] = ["Frappe.Identity.Application", "Frappe.Identity.Contracts", "Frappe.Notifications.Domain"],
            ["Frappe.Notifications.Triggers.Identity"] = ["Frappe.Identity.Contracts", "Frappe.Identity.Domain"],
            ["Frappe.Http"] = [],
        };

        // Act
        var offenders = ArchitectureRules.CrossModuleOffenders(graph, modules, CrossModuleAllowList);

        // Assert
        await Assert.That(offenders).IsEquivalentTo(
        [
            "Frappe.Notifications.Application -> Frappe.Identity.Application",
            "Frappe.Notifications.Application -> Frappe.Identity.Contracts",
            "Frappe.Notifications.Triggers.Identity -> Frappe.Identity.Domain",
        ]);
    }

    /// <summary>ARCH-05: the reviewed pairs (assembly, module reached through its Contracts). Every new line is a decision.</summary>
    private static readonly (string Assembly, string Module)[] CrossModuleAllowList =
    [
        ("Frappe.Notifications.Triggers.Identity", "Identity"),

        // Access.Contracts is the authorization vocabulary every module's use cases and endpoints declare their rules with.
        ("Frappe.Identity.Application", "Access"),
        ("Frappe.Identity.Http", "Access"),
        ("Frappe.Identity.Infrastructure", "Access"),
        ("Frappe.Notifications.Application", "Access"),
    ];

    [Test]
    public async Task A_module_contract_exposes_nothing_but_its_public_types()
    {
        // Arrange
        // Access.Contracts is the rule-authoring vocabulary of the authorizer (rule classes and combinators), not a set of messages: only
        // its isolation (no reference to another project of any module) is checked, not its shape.
        string[] authoringContracts = ["Access"];
        var contracts = ModuleDoorTests.AllModules.ToDictionary(
            module => module, module => System.Reflection.Assembly.Load($"Frappe.{module}.Contracts"));

        // Act
        var shape = contracts.Where(entry => !authoringContracts.Contains(entry.Key))
            .SelectMany(entry => ArchitectureRules.ContractShapeOffenders(entry.Value.GetTypes(), entry.Key))
            .ToArray();
        var inside = contracts.SelectMany(entry => Scope(entry.Value).ShouldNot().HaveDependencyOnAny(
                [.. ModuleDoorTests.AllModules.SelectMany(module => new[] { "Domain", "Application", "Infrastructure", "Http" }
                    .Select(layer => $"Frappe.{module}.{layer}"))]).GetResult().FailingTypeNames ?? [])
            .ToArray();
        var seen = ArchitectureRules.ContractShapeOffenders(FixtureAssembly.GetTypes().Where(type => type.Name == "RuleBreakers"), "Identity");
        var instrumentation = ArchitectureRules.ContractShapeOffenders(
            [PublicTypeIn("Microsoft.CodeCoverage.Instrumentation.Static.Tracker"), PublicTypeIn("Elsewhere")], "Identity");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(shape).IsEmpty()
                .Because($"a Contracts type is not a public record, interface or enum of its module: {string.Join(", ", shape)}");
            await Assert.That(inside).IsEmpty().Because($"a Contracts type names the inside of a module: {string.Join(", ", inside)}");
            await Assert.That(seen).Contains("Frappe.Architecture.Tests.RuleBreakers");
            await Assert.That(instrumentation).IsEquivalentTo(["Elsewhere.Tracker"]);
        }
    }

    private static Type PublicTypeIn(string ns)
    {
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName(ns), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        return assembly.DefineDynamicModule("m")
            .DefineType($"{ns}.Tracker", System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class).CreateType();
    }

    [Test]
    public async Task Modules_contain_no_tracing_or_exporter_code()
    {
        // Arrange
        var graph = ArchitectureRules.ProductionGraph;
        var assemblies = ArchitectureRules.ModuleAssemblies(graph.Keys, ModuleDoorTests.AllModules)
            .Select(name => System.Reflection.Assembly.Load(name));

        // Act
        var offenders = assemblies.SelectMany(assembly => TracingTypes(Scope(assembly))).ToArray();
        var unmarked = ArchitectureRules.UnmarkedTracingTypes(offenders, SourceOf);
        var seen = ArchitectureRules.UnmarkedTracingTypes(TracingTypes(Fixtures()), SourceOf);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(unmarked).IsEmpty()
                .Because($"a module creates an ActivitySource, tracer or exporter without `// Exception to OBS-01`: {string.Join(", ", unmarked)}");
            await Assert.That(offenders).Contains("Frappe.Notifications.Application.Telemetry.NotificationsTelemetry");
            await Assert.That(seen).Contains("Frappe.Architecture.Tests.RuleBreakers");
        }
    }

    private static IEnumerable<string> TracingTypes(PredicateList scope) =>
        scope.ShouldNot().HaveDependencyOnAny("System.Diagnostics.ActivitySource", "OpenTelemetry").GetResult().FailingTypeNames ?? [];

    private static string? SourceOf(string fullName)
    {
        var simple = fullName[(fullName.LastIndexOf('.') + 1)..];
        var parts = fullName.Split('.');
        var project = Enumerable.Range(3, Math.Max(0, parts.Length - 3)).Select(count => string.Join('.', parts.Take(count))).ToArray();
        var file = Directory.EnumerateFiles(Path.Combine(RepositoryRoot.FullPath, "src", "Modules"), $"{simple}.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && path.Split(Path.DirectorySeparatorChar).Any(segment => project.Contains(segment)));
        return file is null ? null : File.ReadAllText(file);
    }

    [Test]
    public async Task Application_ports_leave_saving_to_the_unit_of_work()
    {
        // Act
        var offenders = ModuleAssembliesIn("Application").SelectMany(assembly => ArchitectureRules.PortsThatSave(assembly.GetTypes())).ToArray();
        var seen = ArchitectureRules.PortsThatSave(FixtureAssembly.GetTypes());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(offenders).IsEmpty().Because($"an Application port declares SaveChangesAsync: {string.Join(", ", offenders)}");
            await Assert.That(seen).IsEquivalentTo(["Frappe.Architecture.Tests.ISavingPortBreaker"]);
        }
    }

    [Test]
    public async Task Platform_never_references_a_module_ARCH_18()
    {
        // Arrange
        var graph = ArchitectureRules.ProductionGraph;
        var violating = new Dictionary<string, string[]> { ["Frappe.Caching"] = ["Frappe.Identity.Contracts"], ["Frappe.Api"] = ["Frappe.Identity"] };

        // Act
        var offenders = ArchitectureRules.PlatformReferencingModules(graph, ModuleDoorTests.AllModules);
        var seen = ArchitectureRules.PlatformReferencingModules(violating, ModuleDoorTests.AllModules);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(offenders).IsEmpty().Because($"a platform project references a module: {string.Join(", ", offenders)}");
            await Assert.That(graph.Keys).Contains("Frappe.Persistence");
            await Assert.That(seen).IsEquivalentTo(["Frappe.Caching -> Frappe.Identity.Contracts"]);
        }
    }

    [Test]
    public async Task The_Identity_producer_reaches_Identity_only_through_its_contracts_ARCH_05()
    {
        // Arrange
        var producer = typeof(Frappe.Notifications.Triggers.Identity.IdentityTriggersModule).Assembly;

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
            [NotificationsHttp, NotificationsInfrastructure, typeof(Frappe.Notifications.Messages.Account.AccountMessagesModule).Assembly];
        for (var index = 0; index < assemblies.Count; index++)
        {
            foreach (var reference in assemblies[index].GetReferencedAssemblies())
            {
                if (reference.Name is { } name
                    && name.StartsWith("Frappe.Notifications.", StringComparison.Ordinal)
                    && !name.StartsWith("Frappe.Notifications.Triggers.", StringComparison.Ordinal)
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
                .Contains("Frappe.Notifications.Domain").And.Contains("Frappe.Notifications.Messages.Account");
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
