using System.Runtime.CompilerServices;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The pure checks behind the architecture rules ARCH-05, ARCH-06, ARCH-12, ARCH-13 and ARCH-18. Each takes the facts it judges as arguments
/// so a test can run it on the real assemblies and on a small violating fixture (the rule can see).
/// </summary>
internal static class ArchitectureRules
{
    private static readonly string[] Hosts = ["Frappe.Api", "Frappe.Worker", "Frappe.Migrator"];

    /// <summary>Every <c>Frappe.*</c> assembly the three hosts load, with what each references.</summary>
    internal static Dictionary<string, string[]> ProductionGraph { get; } = BuildGraph();

    /// <summary>The module <paramref name="assembly"/> belongs to (<c>Frappe.M</c> or <c>Frappe.M.*</c>), or null for platform and hosts.</summary>
    internal static string? ModuleOf(string assembly, IEnumerable<string> modules) =>
        modules.FirstOrDefault(module => assembly == $"Frappe.{module}" || assembly.StartsWith($"Frappe.{module}.", StringComparison.Ordinal));

    /// <summary>Names of the production assemblies of the modules (doors, layers and plug-ins).</summary>
    internal static string[] ModuleAssemblies(IEnumerable<string> assemblies, IReadOnlyCollection<string> modules) =>
        [.. assemblies.Where(name => ModuleOf(name, modules) is not null).Order(StringComparer.Ordinal)];

    /// <summary>ARCH-05: references from an assembly of one module to another module that are not an allow-listed Contracts reference.</summary>
    internal static string[] CrossModuleOffenders(
        IReadOnlyDictionary<string, string[]> graph,
        IReadOnlyCollection<string> modules,
        IReadOnlyCollection<(string Assembly, string Module)> allowed) =>
    [
        .. graph.SelectMany(entry => entry.Value
                .Where(reference => ModuleOf(entry.Key, modules) is { } own
                    && ModuleOf(reference, modules) is { } other
                    && other != own
                    && !(reference == $"Frappe.{other}.Contracts" && allowed.Contains((entry.Key, other))))
                .Select(reference => $"{entry.Key} -> {reference}"))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>ARCH-18: references from a platform assembly (not a module, not a host) to any module assembly.</summary>
    internal static string[] PlatformReferencingModules(IReadOnlyDictionary<string, string[]> graph, IReadOnlyCollection<string> modules) =>
    [
        .. graph.Where(entry => ModuleOf(entry.Key, modules) is null && !Hosts.Contains(entry.Key))
            .SelectMany(entry => entry.Value
                .Where(reference => ModuleOf(reference, modules) is not null)
                .Select(reference => $"{entry.Key} -> {reference}"))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>ARCH-13: the interfaces of <paramref name="types"/> that declare a method named <c>SaveChanges*</c>.</summary>
    internal static string[] PortsThatSave(IEnumerable<Type> types) =>
    [
        .. types.Where(type => type.IsInterface)
            .Where(type => type.GetMethods().Concat(type.GetInterfaces().SelectMany(inherited => inherited.GetMethods()))
                .Any(method => method.Name.StartsWith("SaveChanges", StringComparison.Ordinal)))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// ARCH-12: the types among <paramref name="offenders"/> whose source file (<paramref name="sourceOf"/> by full type name) does not carry the
    /// <c>// Exception to OBS-01</c> marker.
    /// </summary>
    internal static string[] UnmarkedTracingTypes(IEnumerable<string> offenders, Func<string, string?> sourceOf) =>
    [
        .. offenders
            .Select(name => name.Split('+')[0])
            .Distinct(StringComparer.Ordinal)
            .Where(name => sourceOf(name)?.Contains("// Exception to OBS-01", StringComparison.Ordinal) != true)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>ARCH-06: the types of a Contracts assembly that are not a public record, interface or enum of the module's own namespace.</summary>
    internal static string[] ContractShapeOffenders(IEnumerable<Type> types, string module) =>
    [
        .. types.Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && !type.IsNested)
            // The CI coverage run injects Microsoft.CodeCoverage tracker types into every instrumented assembly.
            .Where(type => !(type.Namespace ?? string.Empty).StartsWith("Microsoft.CodeCoverage", StringComparison.Ordinal))
            .Where(type => !type.IsPublic
                || !(type.Namespace ?? string.Empty).StartsWith($"Frappe.{module}.Contracts", StringComparison.Ordinal)
                || !(type.IsInterface || type.IsEnum || IsRecord(type)))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal),
    ];

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$") is not null;

    private static Dictionary<string, string[]> BuildGraph()
    {
        var graph = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Type[] hosts = [typeof(Frappe.Api.ApiComposition), typeof(Frappe.Worker.WorkerComposition), typeof(Frappe.Migrator.MigratorComposition)];
        foreach (var host in hosts)
        {
            foreach (var entry in HostReferenceTests.Closure(host.Assembly))
            {
                graph[entry.Key] = entry.Value;
            }
        }

        return graph;
    }
}
