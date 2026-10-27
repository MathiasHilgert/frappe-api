using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Frappe.Platform;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace Frappe.ArchitectureTests;

public sealed class DependencyRuleTests
{
    // Layers are selected by assembly: the Host's top-level Program lives in the global namespace.
    private static readonly Assembly _kernel = Assembly.Load("Frappe.Kernel");
    private static readonly Assembly _platform = typeof(PlatformExtensions).Assembly;
    private static readonly Assembly _host = typeof(Program).Assembly;

    private static readonly Architecture _architecture = new ArchLoader()
        .LoadAssemblies(_kernel, _platform, _host)
        .Build();

    [Theory]
    [InlineData(@"^Microsoft\.AspNetCore(\..+)?$")]
    [InlineData(@"^Microsoft\.EntityFrameworkCore(\..+)?$")]
    [InlineData(@"^Npgsql(\..+)?$")]
    [InlineData(@"^Wolverine(\..+)?$")]
    [InlineData(@"^Quartz(\..+)?$")]
    public void The_kernel_depends_on_no_infrastructure_library(string forbiddenNamespace)
    {
        Types()
            .That()
            .ResideInAssembly(_kernel)
            .Should()
            .NotDependOnAny(Types().That().ResideInNamespaceMatching(forbiddenNamespace))
            // shortcut: the Kernel has no types yet, so the rule may match nothing; drop this once it does.
            .WithoutRequiringPositiveResults()
            .Check(_architecture);
    }

    [Fact]
    public void The_kernel_depends_on_neither_the_platform_nor_the_host()
    {
        Types()
            .That()
            .ResideInAssembly(_kernel)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(_platform).Or().ResideInAssembly(_host))
            // shortcut: the Kernel has no types yet, so the rule may match nothing; drop this once it does.
            .WithoutRequiringPositiveResults()
            .Check(_architecture);
    }

    [Fact]
    public void The_platform_does_not_depend_on_the_host()
    {
        Types()
            .That()
            .ResideInAssembly(_platform)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(_host))
            .Check(_architecture);
    }
}
