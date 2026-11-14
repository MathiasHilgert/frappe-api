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
    private static readonly Assembly GeoDomain = System.Reflection.Assembly.Load("Frappe.Geo.Domain");
    private static readonly Assembly GeoApplication = System.Reflection.Assembly.Load("Frappe.Geo.Application");
    private static readonly Assembly GeoContracts = System.Reflection.Assembly.Load("Frappe.Geo.Contracts");
    private static readonly Assembly GeoInfrastructure = System.Reflection.Assembly.Load("Frappe.Geo.Infrastructure");

    /// <summary>The source packages: what serves any country, and one package per country.</summary>
    private static readonly Assembly[] GeoSourcePackages =
        [System.Reflection.Assembly.Load("Frappe.Geo.Global")];

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
    [Arguments("Frappe.Geo.Domain")]
    [Arguments("Frappe.Geo.Application")]
    public async Task Inner_layers_know_nothing_about_infrastructure(string assemblyName)
    {
        var assembly = new[] { Results, Localization, Aggregates, IntegrationEvents, GeoDomain, GeoApplication }
            .Single(a => a.GetName().Name == assemblyName);

        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(Infrastructure).GetResult();

        await AssertAsync(result, $"{assemblyName} depends on infrastructure");
    }

    [Test]
    public async Task The_domain_does_not_depend_on_its_use_cases()
    {
        var result = Types.InAssembly(GeoDomain).ShouldNot().HaveDependencyOnAny("Frappe.Geo.Application", "Frappe.UseCases").GetResult();

        await AssertAsync(result, "Geo.Domain depends on the application layer");
    }

    [Test]
    public async Task A_module_contract_exposes_nothing_but_its_public_types()
    {
        var result = Types.InAssembly(GeoContracts)
            .ShouldNot().HaveDependencyOnAny("Frappe.Geo.Domain", "Frappe.Geo.Application", "Frappe.Geo.Infrastructure", "NodaTime")
            .GetResult();

        await AssertAsync(result, "Geo.Contracts leaks module internals");
    }

    [Test]
    public async Task Modules_contain_no_tracing_or_exporter_code()
    {
        // S22: infrastructure telemetry is the platform's job. Modules may declare business metrics through
        // IMeterFactory (System.Diagnostics.Metrics), so meters are allowed; spans and OpenTelemetry are not.
        var result = Types.InAssemblies([GeoDomain, GeoApplication, GeoInfrastructure])
            .ShouldNot().HaveDependencyOnAny("OpenTelemetry", "System.Diagnostics.ActivitySource")
            .GetResult();

        await AssertAsync(result, "a module creates its own spans or exporters instead of relying on the platform");
    }

    [Test]
    public async Task Infrastructure_adapters_do_not_serve_http()
    {
        var result = Types.InAssembly(GeoInfrastructure).ShouldNot().HaveDependencyOn("Microsoft.AspNetCore").GetResult();

        await AssertAsync(result, "Geo.Infrastructure depends on ASP.NET Core");
    }

    [Test]
    public async Task Application_ports_leave_saving_to_the_unit_of_work()
    {
        // S6: the platform commits each command once (IUnitOfWork); a port that saves would commit half a command.
        var saving = GeoApplication.GetTypes()
            .Where(type => type.IsInterface)
            .Where(port => port.GetMethods().Any(method => method.Name == "SaveChangesAsync"))
            .Select(port => port.FullName)
            .ToArray();

        await Assert.That(saving).IsEmpty().Because($"ports declaring SaveChangesAsync: {string.Join(", ", saving)}");
    }

    [Test]
    public async Task Source_packages_declare_and_never_reach_the_pipeline_or_the_database()
    {
        // S16: a country or global package declares where its data is and how to read it; downloading, applying,
        // storing and scheduling belong to Geo's core, which never references a package.
        var result = Types.InAssemblies(GeoSourcePackages)
            .ShouldNot().HaveDependencyOnAny("Frappe.Geo.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "Quartz", "Frappe.Persistence", "Frappe.Messaging")
            .GetResult();

        await AssertAsync(result, "a source package reaches into Geo's core or its storage");
    }

    [Test]
    public async Task Geo_core_never_knows_a_source_package()
    {
        var result = Types.InAssemblies([GeoDomain, GeoApplication, GeoInfrastructure])
            .ShouldNot().HaveDependencyOnAny("Frappe.Geo.Global", "Frappe.Geo.Countries")
            .GetResult();

        await AssertAsync(result, "Geo's core references a source package");
    }

    [Test]
    public async Task The_rules_can_see_real_dependencies()
    {
        // Positive control: a rule that can never fail would prove nothing.
        var persistence = Types.InAssembly(Persistence).That().HaveDependencyOn("Microsoft.EntityFrameworkCore").GetTypes();

        await Assert.That(persistence.Any()).IsTrue();
    }
}
