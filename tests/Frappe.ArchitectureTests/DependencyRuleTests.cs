using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Frappe.Platform;
using Frappe.Platform.Health;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace Frappe.ArchitectureTests;

public sealed partial class DependencyRuleTests
{
    // Layers are selected by assembly: the Host's top-level Program lives in the global namespace.
    private static readonly Assembly _kernel = Assembly.Load("Frappe.Kernel");
    private static readonly Assembly _platform = typeof(PlatformExtensions).Assembly;
    private static readonly Assembly _health = typeof(HealthExtensions).Assembly;
    private static readonly Assembly _host = typeof(Program).Assembly;

    // Every Frappe.Platform* project the Host ships, found by file so a new one is covered at once.
    private static readonly Assembly[] _platformProjects =
    [
        .. Directory
            .GetFiles(AppContext.BaseDirectory, "Frappe.Platform*.dll")
            .Order(StringComparer.Ordinal)
            .Select(static file => Assembly.Load(Path.GetFileNameWithoutExtension(file))),
    ];

    // Every module project the Host ships, Frappe.<Module>.<Layer>, found by file so a new module is covered at once.
    private static readonly Assembly[] _moduleProjects =
    [
        .. Directory
            .GetFiles(AppContext.BaseDirectory, "Frappe.*.dll")
            .Select(static file => Path.GetFileNameWithoutExtension(file))
            .Where(static name => ModuleProject().IsMatch(name))
            .Order(StringComparer.Ordinal)
            .Select(static name => Assembly.Load(name)),
    ];

    private static readonly Architecture _architecture = new ArchLoader()
        .LoadAssemblies([_kernel, _host, .. _platformProjects, .. _moduleProjects])
        .Build();

    public static TheoryData<string> Modules =>
        [
            .. _moduleProjects
                .Select(static assembly =>
                    ModuleProject().Match(assembly.GetName().Name ?? "").Groups["module"].Value
                )
                .Distinct(StringComparer.Ordinal),
        ];

    [Theory]
    [InlineData(@"^Microsoft\.AspNetCore(\..+)?$")]
    [InlineData(@"^Microsoft\.EntityFrameworkCore(\..+)?$")]
    [InlineData(@"^Npgsql(\..+)?$")]
    [InlineData(@"^Wolverine(\..+)?$")]
    [InlineData(@"^Quartz(\..+)?$")]
    public void The_kernel_depends_on_no_infrastructure_library(string forbiddenNamespace)
    {
        FrappeTypes()
            .ResideInAssembly(_kernel)
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(forbiddenNamespace)
            .Check(_architecture);
    }

    [Fact]
    public void The_kernel_depends_on_neither_the_platform_nor_the_host()
    {
        FrappeTypes()
            .ResideInAssembly(_kernel)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(_host, _platformProjects))
            .Check(_architecture);
    }

    [Fact]
    public void The_platform_does_not_depend_on_the_host()
    {
        FrappeTypes()
            .ResideInAssembly(_platform)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(_host))
            .Check(_architecture);
    }

    [Fact]
    public void Health_depends_on_neither_the_platform_composition_nor_the_host()
    {
        FrappeTypes()
            .ResideInAssembly(_health)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(_platform).Or().ResideInAssembly(_host))
            .Check(_architecture);
    }

    [Theory]
    [InlineData("Frappe.Platform.Hosting")]
    [InlineData("Frappe.Platform.Health")]
    [InlineData("Frappe.Platform.Persistence")]
    [InlineData("Frappe.Platform.Modularity")]
    [InlineData("Frappe.Platform.Messaging")]
    public void A_platform_project_depends_on_neither_the_composition_nor_the_host(string project)
    {
        FrappeTypes()
            .ResideInAssembly(Assembly.Load(project))
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(_platform).Or().ResideInAssembly(_host))
            .Check(_architecture);
    }

    [Theory]
    [InlineData("Frappe.Platform.Hosting", "Frappe.Platform.Health")]
    [InlineData("Frappe.Platform.Hosting", "Frappe.Platform.Persistence")]
    [InlineData("Frappe.Platform.Health", "Frappe.Platform.Hosting")]
    [InlineData("Frappe.Platform.Health", "Frappe.Platform.Persistence")]
    public void Hosting_and_health_depend_on_no_other_platform_project(string project, string other)
    {
        FrappeTypes()
            .ResideInAssembly(Assembly.Load(project))
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(Assembly.Load(other)))
            .Check(_architecture);
    }

    // The bus stays swappable behind the Kernel ports: one adapter project knows Wolverine.
    [Theory]
    [InlineData("Frappe.Platform")]
    [InlineData("Frappe.Platform.Hosting")]
    [InlineData("Frappe.Platform.Health")]
    [InlineData("Frappe.Platform.Persistence")]
    [InlineData("Frappe.Platform.Modularity")]
    [InlineData("Frappe.Host")]
    public void Only_the_messaging_adapter_depends_on_wolverine(string project)
    {
        FrappeTypes()
            .ResideInAssembly(Assembly.Load(project))
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^Wolverine(\..+)?$")
            .Check(_architecture);
    }

    [Fact]
    public void The_module_rules_see_the_shipped_modules()
    {
        Assert.Contains(
            "Frappe.Geo.Infrastructure",
            _moduleProjects.Select(static assembly => assembly.GetName().Name),
            StringComparer.Ordinal
        );
    }

    [Theory]
    [InlineData("Domain", @"^Microsoft\.AspNetCore(\..+)?$")]
    [InlineData("Domain", @"^Microsoft\.EntityFrameworkCore(\..+)?$")]
    [InlineData("Domain", @"^Npgsql(\..+)?$")]
    [InlineData("Domain", @"^Wolverine(\..+)?$")]
    [InlineData("Domain", @"^Quartz(\..+)?$")]
    [InlineData("Domain", @"^Frappe\.Platform(\..+)?$")]
    [InlineData("Application", @"^Microsoft\.AspNetCore(\..+)?$")]
    [InlineData("Application", @"^Microsoft\.EntityFrameworkCore(\..+)?$")]
    [InlineData("Application", @"^Npgsql(\..+)?$")]
    [InlineData("Application", @"^Wolverine(\..+)?$")]
    [InlineData("Application", @"^Quartz(\..+)?$")]
    [InlineData("Application", @"^Frappe\.Platform(\..+)?$")]
    [InlineData("Infrastructure", @"^Wolverine(\..+)?$")]
    [InlineData("Infrastructure", @"^Quartz(\..+)?$")]
    public void A_module_layer_depends_on_no_library_outside_it(
        string layer,
        string forbiddenNamespace
    )
    {
        FrappeTypes()
            .ResideInAssembly(Layer(layer)[0], Layer(layer)[1..])
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(forbiddenNamespace)
            .Check(_architecture);
    }

    // Domain is the core, Application builds on it, and Infrastructure on both; never the other way around.
    [Theory]
    [InlineData("Domain", "Application")]
    [InlineData("Domain", "Infrastructure")]
    [InlineData("Application", "Infrastructure")]
    public void An_inner_module_layer_does_not_depend_on_an_outer_one(string inner, string outer)
    {
        FrappeTypes()
            .ResideInAssembly(Layer(inner)[0], Layer(inner)[1..])
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(Layer(outer)[0], Layer(outer)[1..]))
            .Check(_architecture);
    }

    // Contracts are a module's only public surface; the Kernel's Frappe.Kernel.Domain is not a module.
    [Theory]
    [MemberData(nameof(Modules))]
    public void A_module_depends_on_another_module_only_through_its_contracts(string module)
    {
        FrappeTypes()
            .ResideInNamespaceMatching($@"^Frappe\.{module}(\..+)?$")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(
                $@"^Frappe\.(?!{module}\.|Kernel\.|Platform\.)[A-Za-z0-9]+\.(Domain|Application|Infrastructure)(\..+)?$"
            )
            .Check(_architecture);
    }

    // This project references only the Host, so the file is here only when a shipped project
    // references the migrations.
    [Fact]
    public void No_shipped_project_references_the_migrations()
    {
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "Frappe.Migrations.dll")));
    }

    [Theory]
    [InlineData(@"^Microsoft\.AspNetCore(\..+)?$")]
    [InlineData(@"^Wolverine(\..+)?$")]
    [InlineData(@"^NATS(\..+)?$")]
    [InlineData(@"^Quartz(\..+)?$")]
    [InlineData(@"^StackExchange\.Redis(\..+)?$")]
    public void Persistence_depends_on_no_other_infrastructure_library(string forbiddenNamespace)
    {
        FrappeTypes()
            .ResideInAssembly(Assembly.Load("Frappe.Platform.Persistence"))
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(forbiddenNamespace)
            .Check(_architecture);
    }

    // JasperFx's source generator, which WolverineFx flows into every project above it, emits the same
    // JasperFx.Generated type into each assembly; ArchUnitNET merges them into one type that seems to cross
    // every boundary, so the rules leave that generated namespace out.
    private static GivenTypesThat FrappeTypes() =>
        Types().That().DoNotResideInNamespace("JasperFx.Generated").And();

    private static Assembly[] Layer(string layer) =>
        [
            .. _moduleProjects.Where(assembly =>
                (assembly.GetName().Name ?? "").EndsWith($".{layer}", StringComparison.Ordinal)
            ),
        ];

    [GeneratedRegex(
        @"^Frappe\.(?<module>[A-Z][A-Za-z0-9]*)\.(Domain|Application|Infrastructure|Contracts)\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 100
    )]
    private static partial Regex ModuleProject();
}
