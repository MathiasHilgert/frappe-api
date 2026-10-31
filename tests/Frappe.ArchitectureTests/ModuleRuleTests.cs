using System.Reflection;
using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;
using Type = System.Type;

namespace Frappe.ArchitectureTests;

// The module boundary rules: layers free of infrastructure libraries, modules meeting only through Contracts, and
// each module mapping only its own data.
public sealed partial class ModuleRuleTests
{
    // Every module project the Host ships, found by file so a new module is covered at once.
    private static readonly Assembly[] _modules =
    [
        .. Directory
            .GetFiles(AppContext.BaseDirectory, "Frappe.*.dll")
            .Select(static file => Path.GetFileNameWithoutExtension(file))
            .Where(static name => ModuleProject().IsMatch(name))
            .Order(StringComparer.Ordinal)
            .Select(static name => Assembly.Load(name)),
    ];

    private static readonly Architecture _architecture = new ArchLoader()
        .LoadAssemblies(_modules)
        .Build();

    private static readonly Assembly[] _innerLayers = [.. Layer("Domain"), .. Layer("Application")];

    public static TheoryData<string> ForbiddenNamespaces =>
        [
            @"^Microsoft\.EntityFrameworkCore(\..+)?$",
            @"^Npgsql(\..+)?$",
            @"^Wolverine(\..+)?$",
            @"^Quartz(\..+)?$",
            @"^Microsoft\.AspNetCore(\..+)?$",
        ];

    [Fact]
    public void The_scan_finds_the_geo_module()
    {
        string[] names = [.. _modules.Select(static assembly => assembly.GetName().Name ?? "")];

        Assert.Contains("Frappe.Geo.Domain", names, StringComparer.Ordinal);
        Assert.Contains("Frappe.Geo.Application", names, StringComparer.Ordinal);
        Assert.Contains("Frappe.Geo.Infrastructure", names, StringComparer.Ordinal);
        Assert.Contains("Frappe.Geo.Contracts", names, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ForbiddenNamespaces))]
    public void Domain_and_application_depend_on_no_infrastructure_library(
        string forbiddenNamespace
    )
    {
        FrappeTypes()
            .ResideInAssembly(_innerLayers[0], _innerLayers)
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(forbiddenNamespace)
            .Check(_architecture);
    }

    [Fact]
    public void Contracts_depend_only_on_the_kernel_error_or_and_noda_time()
    {
        Assembly[] contracts = Layer("Contracts");

        FrappeTypes()
            .ResideInAssembly(contracts[0], contracts)
            .Should()
            .OnlyDependOnTypesThat()
            .ResideInNamespaceMatching(
                @"^(System(\..+)?|Frappe\.Kernel(\..+)?|Frappe\.[A-Za-z]+\.Contracts(\..+)?|ErrorOr|NodaTime(\..+)?)$"
            )
            .Check(_architecture);
    }

    // Another module's Domain, Application or Infrastructure is never reachable; only its Contracts are.
    [Fact]
    public void A_module_reaches_another_module_only_through_its_contracts()
    {
        string[] violations =
        [
            .. _modules.SelectMany(static assembly =>
                assembly
                    .GetReferencedAssemblies()
                    .Select(static reference => reference.Name ?? "")
                    .Where(reference =>
                        ModuleProject().IsMatch(reference)
                        && !reference.EndsWith(".Contracts", StringComparison.Ordinal)
                        && !string.Equals(
                            Module(reference),
                            Module(assembly.GetName().Name ?? ""),
                            StringComparison.Ordinal
                        )
                    )
                    .Select(reference => $"{assembly.GetName().Name} references {reference}")
            ),
        ];

        Assert.Empty(violations);
    }

    // Ports may later move to the database or HTTP, so they take Kernel value objects, never strings, and are async.
    [Fact]
    public void Every_contracts_port_takes_no_string_returns_a_value_task_of_error_or_and_ends_with_a_cancellation_token()
    {
        MethodInfo[] ports =
        [
            .. Layer("Contracts")
                .SelectMany(static assembly => assembly.GetExportedTypes())
                .Where(static type => type.IsInterface)
                .SelectMany(static type => type.GetMethods()),
        ];

        Assert.NotEmpty(ports);
        Assert.Empty(
            ports
                .Where(static method => !IsPortShaped(method))
                .Select(static method => $"{method.DeclaringType?.FullName}.{method.Name}")
        );
    }

    // A module's context maps only its own entities.
    [Fact]
    public void A_module_configures_only_its_own_entities()
    {
        string[] violations =
        [
            .. Layer("Infrastructure")
                .SelectMany(static assembly => assembly.GetTypes())
                .SelectMany(static type =>
                    type.GetInterfaces()
                        .Where(static contract =>
                            contract.IsGenericType
                            && string.Equals(
                                contract.GetGenericTypeDefinition().FullName,
                                "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1",
                                StringComparison.Ordinal
                            )
                        )
                        .Select(contract => (Type: type, Entity: contract.GenericTypeArguments[0]))
                )
                .Where(static mapped =>
                    !string.Equals(
                        Module(mapped.Entity.Assembly.GetName().Name ?? ""),
                        Module(mapped.Type.Assembly.GetName().Name ?? ""),
                        StringComparison.Ordinal
                    )
                )
                .Select(static mapped =>
                    $"{mapped.Type.FullName} configures {mapped.Entity.FullName}"
                ),
        ];

        Assert.Empty(violations);
    }

    // An invalid value object must not exist, so only its Create builds one.
    [Fact]
    public void Module_value_objects_and_typed_ids_have_no_public_constructor()
    {
        Type[] valueObjects = [.. ValueObjects()];

        Assert.NotEmpty(valueObjects);
        Assert.Empty(
            valueObjects
                .Where(static type => type.GetConstructors().Length > 0)
                .Select(static type => type.FullName)
        );
    }

    // A concept with an owner lives with its owner, so the Kernel never redefines a module's value object.
    [Fact]
    public void The_kernel_holds_no_module_owned_type()
    {
        HashSet<string> owned = [.. ValueObjects().Select(static type => type.Name)];

        Assert.Empty(
            Assembly
                .Load("Frappe.Kernel")
                .GetTypes()
                .Where(type => owned.Contains(type.Name))
                .Select(static type => type.FullName)
        );
    }

    // A Domain may use another module's published values, never its ports, events or DTOs.
    [Fact]
    public void A_domain_uses_another_module_only_through_its_published_value_objects()
    {
        foreach (Assembly domain in Layer("Domain"))
        {
            string module = Module(domain.GetName().Name ?? "");
            FrappeTypes()
                .ResideInAssembly(domain)
                .Should()
                .NotDependOnAnyTypesThat()
                .ResideInNamespaceMatching(
                    $@"^Frappe\.(?!{module}\.)[A-Z][A-Za-z]+\.Contracts(?!\.ValueObjects(\..+)?$)(\..+)?$"
                )
                .WithoutRequiringPositiveResults()
                .Check(_architecture);
        }
    }

    // Published value objects and typed ids: everything under a module's Contracts.ValueObjects namespace.
    private static IEnumerable<Type> ValueObjects() =>
        Layer("Contracts")
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type =>
                type.Namespace?.EndsWith(".Contracts.ValueObjects", StringComparison.Ordinal)
                == true
            );

    private static bool IsPortShaped(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        Type result = method.ReturnType;
        return parameters is [.., { ParameterType: var last }]
            && last == typeof(CancellationToken)
            && Array.TrueForAll(
                parameters,
                static parameter => parameter.ParameterType != typeof(string)
            )
            && result.IsGenericType
            && result.GetGenericTypeDefinition() == typeof(ValueTask<>)
            && result.GenericTypeArguments[0] is { IsGenericType: true } inner
            && inner.GetGenericTypeDefinition() == typeof(ErrorOr.ErrorOr<>);
    }

    private static Assembly[] Layer(string layer) =>
        [
            .. _modules.Where(assembly =>
                assembly.GetName().Name?.EndsWith($".{layer}", StringComparison.Ordinal) == true
            ),
        ];

    // Frappe.Geo.Domain → Geo.
    private static string Module(string assemblyName) =>
        ModuleProject().Match(assemblyName) is { Success: true } match
            ? match.Groups["module"].Value
            : assemblyName;

    // JasperFx's generator emits one shared type into every assembly above WolverineFx; it is not module code.
    private static GivenTypesThat FrappeTypes() =>
        Types().That().DoNotResideInNamespace("JasperFx.Generated").And();

    [GeneratedRegex(
        @"^Frappe\.(?<module>(?!Platform|Kernel|Host|Migrations)[A-Z][A-Za-z]+)\.(Domain|Application|Infrastructure|Contracts)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        1000
    )]
    private static partial Regex ModuleProject();
}
