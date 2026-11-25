using System.Text.RegularExpressions;
using System.Xml.Linq;
using Frappe.Aggregates;
using Frappe.Testing;
using Frappe.UseCases;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The pure checks behind the structure rules STR-01, STR-04, STR-05, STR-07, STR-08 and STR-10. Each takes the facts it judges (project paths
/// relative to <c>src/Modules</c>, types, <c>InternalsVisibleTo</c> declarations) so a test can run it on the real solution and on a small
/// violating fixture (the rule can see).
/// </summary>
internal static partial class StructureRules
{
    /// <summary>The layer projects every module has next to its door (STR-01).</summary>
    private static readonly string[] Layers = ["Domain", "Application", "Contracts", "Infrastructure", "Http"];

    /// <summary>The kinds a plug-in package may be named after (STR-04).</summary>
    private static readonly string[] Kinds = ["Messages", "Senders", "Triggers", "Limits"];

    /// <summary>Every project under <c>src/Modules</c>, as <c>&lt;M&gt;/&lt;folder&gt;/.../&lt;Name&gt;.csproj</c> with forward slashes.</summary>
    internal static string[] ModuleProjects() =>
    [
        .. Directory.EnumerateFiles(Path.Combine(RepositoryRoot.FullPath, "src", "Modules"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Path.Combine(RepositoryRoot.FullPath, "src", "Modules"), path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>STR-01: the projects a module lacks among its door and five layers (<c>M/Name/Name.csproj</c> each).</summary>
    internal static string[] MissingProjects(IReadOnlyCollection<string> projects, IEnumerable<string> modules) =>
    [
        .. modules
            .SelectMany(module => new[] { $"Frappe.{module}" }.Concat(Layers.Select(layer => $"Frappe.{module}.{layer}"))
                .Select(name => $"{module}/{name}/{name}.csproj"))
            .Where(path => !projects.Contains(path))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// STR-04: the projects that are neither a module's door or layer nor a plug-in at
    /// <c>&lt;M&gt;/Extensions/Frappe.&lt;M&gt;.&lt;Kind&gt;.&lt;Name&gt;</c>.
    /// </summary>
    internal static string[] PlugInOffenders(IEnumerable<string> projects, IEnumerable<string> modules) =>
    [
        .. projects.Where(path => !IsDoorOrLayer(path, modules) && !IsPlugIn(path, modules)).Order(StringComparer.Ordinal),
    ];

    /// <summary>STR-05: the handlers among <paramref name="types"/> not named <c>&lt;Request&gt;Handler</c>.</summary>
    internal static string[] MisnamedHandlers(IEnumerable<Type> types) =>
    [
        .. types.Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() is var port
                    && (port == typeof(ICommandHandler<,>) || port == typeof(IQueryHandler<,>)))
                .Select(contract => (Type: type, Request: contract.GetGenericArguments()[0])))
            .Where(pair => pair.Type.Name != pair.Request.Name + "Handler")
            .Select(pair => pair.Type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>STR-07: the types extending <see cref="AggregateRoot"/> that do not live in a <c>Frappe.&lt;M&gt;.Domain</c> assembly.</summary>
    internal static string[] AggregatesOutsideDomain(IEnumerable<Type> types) =>
    [
        .. types.Where(type => type.IsSubclassOf(typeof(AggregateRoot)))
            .Where(type => !DomainAssembly().IsMatch(type.Assembly.GetName().Name!))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>STR-08: the types named <c>*Row</c> that extend <see cref="AggregateRoot"/>.</summary>
    internal static string[] RowsThatAreAggregates(IEnumerable<Type> types) =>
    [
        .. types.Where(type => type.Name.EndsWith("Row", StringComparison.Ordinal) && type.IsSubclassOf(typeof(AggregateRoot)))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The <c>InternalsVisibleTo</c> items of each project, as <c>Project -&gt; Target</c>.</summary>
    internal static string[] Declarations(IEnumerable<string> projects, Func<string, XDocument> load) =>
    [
        .. projects.SelectMany(path => load(path).Descendants("InternalsVisibleTo")
                .Select(item => (string?)item.Attribute("Include"))
                .Where(target => target is not null)
                .Select(target => $"{Path.GetFileNameWithoutExtension(path)} -> {target}"))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// STR-10: the declarations (<c>Project -&gt; Target</c>) toward anything but the module's own unit and integration tests, its own door from
    /// Infrastructure, its own Infrastructure from Application, or <c>Frappe.Api.IntegrationTests</c> from Http and Infrastructure.
    /// </summary>
    internal static string[] ForbiddenVisibility(IEnumerable<string> declarations, IEnumerable<string> modules) =>
    [
        .. declarations.Where(declaration =>
            {
                var parts = declaration.Split(" -> ");
                return !modules.Any(module => Allowed(module, parts[0], parts[1]));
            })
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>STR-10: the source files that declare <c>InternalsVisibleTo</c> as an assembly attribute.</summary>
    internal static string[] AttributeDeclarations(IEnumerable<string> sources, Func<string, string> read) =>
    [
        .. sources.Where(path => read(path).Contains("InternalsVisibleTo", StringComparison.Ordinal)).Order(StringComparer.Ordinal),
    ];

    private static bool Allowed(string module, string project, string target) =>
        project.StartsWith($"Frappe.{module}", StringComparison.Ordinal)
        && (target == $"Frappe.{module}.UnitTests"
            || target == $"Frappe.{module}.IntegrationTests"
            || (target == $"Frappe.{module}" && project == $"Frappe.{module}.Infrastructure")
            || (target == $"Frappe.{module}.Infrastructure" && project == $"Frappe.{module}.Application")
            || (target == "Frappe.Api.IntegrationTests" && project is var name
                && (name == $"Frappe.{module}.Http" || name == $"Frappe.{module}.Infrastructure")));

    private static bool IsDoorOrLayer(string path, IEnumerable<string> modules) =>
        modules.Any(module => new[] { $"Frappe.{module}" }.Concat(Layers.Select(layer => $"Frappe.{module}.{layer}"))
            .Any(name => path == $"{module}/{name}/{name}.csproj"));

    private static bool IsPlugIn(string path, IEnumerable<string> modules) =>
        modules.Any(module =>
        {
            var name = $"Frappe\\.{module}\\.({string.Join('|', Kinds)})\\.[A-Z][A-Za-z0-9]*";
            return Regex.IsMatch(path, $"^{module}/Extensions/{name}/{name}\\.csproj$", RegexOptions.None, TimeSpan.FromSeconds(1));
        });

    [GeneratedRegex(@"^Frappe\.[A-Za-z]+\.Domain$")]
    private static partial Regex DomainAssembly();
}
