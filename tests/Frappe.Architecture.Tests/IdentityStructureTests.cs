using System.Reflection;
using NetArchTest.Rules;

namespace Frappe.Architecture.Tests;

/// <summary>
/// Identity keeps its authentication core (principals, identifiers, credentials, sessions, codes, limits) apart from the Owners feature
/// that plugs into it, as folders per layer: a new kind of principal must cost the core nothing.
/// </summary>
public sealed class IdentityStructureTests
{
    private static readonly string[] Layers = ["Domain", "Application", "Contracts", "Infrastructure", "Http"];

    /// <summary>
    /// The module-level namespaces: composition, persistence mapping and messaging see every feature, Owners included, so they are not core.
    /// Any other namespace that is not an Owners one counts as core, so a new core folder is covered without touching this test.
    /// </summary>
    private static readonly string[] ModuleLevelNamespaces =
    [
        "Frappe.Identity.Domain",
        "Frappe.Identity.Http",
        "Frappe.Identity.Infrastructure",
        "Frappe.Identity.Infrastructure.Messaging",
        "Frappe.Identity.Infrastructure.Persistence",
        "Frappe.Identity.Infrastructure.Persistence.Migrations",
    ];

    private static readonly string[] OwnersNamespaces = [.. Layers.Select(layer => $"Frappe.Identity.{layer}.Owners")];

    private static readonly Assembly Domain = typeof(Frappe.Identity.Domain.Owners.Owner).Assembly;
    private static readonly Assembly Infrastructure = typeof(Frappe.Identity.Infrastructure.IdentityModule).Assembly;

    private static Assembly[] IdentityAssemblies() =>
    [
        .. Layers.Select(layer => System.Reflection.Assembly.Load($"Frappe.Identity.{layer}")),
        typeof(Frappe.Identity.Limits.Valkey.IdentityLimitsValkeyModule).Assembly,
    ];

    private static bool IsOwners(string name) => name.Split('.').Contains("Owners");

    /// <summary>Every namespace of <paramref name="assembly"/> that is neither Owners nor module-level: the core.</summary>
    private static string[] CoreNamespaces(Assembly assembly) =>
        [.. assembly.GetTypes().Select(type => type.Namespace).OfType<string>()
            .Where(name => name.StartsWith("Frappe.Identity.", StringComparison.Ordinal) && !IsOwners(name) && !ModuleLevelNamespaces.Contains(name))
            .Distinct().Order(StringComparer.Ordinal)];

    /// <summary>The types of <paramref name="assembly"/> in the core.</summary>
    private static string[] CoreTypes(Assembly assembly) =>
        [.. assembly.GetTypes().Where(type => type.Namespace is { } name && CoreNamespaces(assembly).Contains(name))
            .Select(type => type.FullName!).Order(StringComparer.Ordinal)];

    /// <summary>The types of <paramref name="assembly"/> in <paramref name="namespaces"/> that depend on an Owners namespace.</summary>
    private static string[] DependingOnOwners(Assembly assembly, IEnumerable<string> namespaces) =>
        [.. namespaces
            .SelectMany(name => Types.InAssembly(assembly).That().ResideInNamespace(name)
                .ShouldNot().HaveDependencyOnAny(OwnersNamespaces).GetResult().FailingTypeNames ?? [])
            .Distinct().Order(StringComparer.Ordinal)];

    [Test]
    public async Task No_type_of_the_identity_core_depends_on_the_Owners_feature()
    {
        // Act
        var offenders = IdentityAssemblies().SelectMany(assembly => DependingOnOwners(assembly, CoreNamespaces(assembly))).ToArray();

        // Assert
        await Assert.That(offenders).IsEmpty().Because("the core must not know Owners: " + string.Join(", ", offenders));
    }

    [Test]
    public async Task The_rule_can_see_the_core_and_a_module_level_type_that_does_depend_on_Owners()
    {
        // Act
        // Positive control: a rule that can never fail would prove nothing. The database context maps owners, and it sits at module level.
        var domainCore = CoreTypes(Domain);
        var infrastructureCore = CoreTypes(Infrastructure);
        var moduleLevelOffenders = DependingOnOwners(Infrastructure, ["Frappe.Identity.Infrastructure.Persistence"]);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(domainCore).Contains("Frappe.Identity.Domain.Principals.Principal")
                .And.Contains("Frappe.Identity.Domain.Sessions.Session").And.Contains("Frappe.Identity.Domain.Codes.OneTimeCode");
            await Assert.That(infrastructureCore).Contains("Frappe.Identity.Infrastructure.Sessions.SessionConfiguration");
            await Assert.That(moduleLevelOffenders).Contains("Frappe.Identity.Infrastructure.Persistence.IdentityDbContext");
        }
    }
}
