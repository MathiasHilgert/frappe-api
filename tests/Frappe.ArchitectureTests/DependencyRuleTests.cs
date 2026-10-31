using ArchUnitNET.Domain;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Frappe.Platform;
using Frappe.Platform.Health;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace Frappe.ArchitectureTests;

public sealed class DependencyRuleTests
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

    private static readonly Architecture _architecture = new ArchLoader()
        .LoadAssemblies([_kernel, _host, .. _platformProjects])
        .Build();

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
}
