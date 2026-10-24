using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Frappe.Contracts.Abstractions.Messaging;
using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>
/// How modules talk: messages in Contracts carry a stable wire identity, and the module graph built from Contracts
/// references has no cycle. Synchronous queries live in a Contracts <c>Queries</c> folder (see docs/conventions.md).
/// </summary>
public sealed partial class ContractsConventionTests
{
    public static TheoryData<string> ContractsProjects => [.. Solution.OfKind(ProjectKind.ModuleContracts).Select(p => p.Name)];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ContractsProjects))]
    public void Message_HasAStableVersionedIdentityInItsModule(string name)
    {
        var project = Solution.Projects.Single(p => p.Name == name);
        var prefix = $"{project.Module!.ToLowerInvariant()}.";
        var offenders = Messages(project)
            .Where(t => t.GetCustomAttribute<MessageIdentityAttribute>() is not { } identity
                        || !identity.Identity.StartsWith(prefix, StringComparison.Ordinal)
                        || !IdentityShape().IsMatch(identity.Identity))
            .Select(t => t.FullName);

        offenders.ShouldBeEmpty($"Every Contracts message carries [MessageIdentity(\"{prefix}<name>.v<n>\")].");
    }

    [Fact]
    public void MessageIdentities_AreUniqueAcrossModules()
    {
        var duplicates = Solution.OfKind(ProjectKind.ModuleContracts)
            .SelectMany(Messages)
            .Select(t => t.GetCustomAttribute<MessageIdentityAttribute>()?.Identity)
            .OfType<string>()
            .GroupBy(i => i, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        duplicates.ShouldBeEmpty();
    }

    [Fact]
    public void ModuleGraph_ThroughContracts_HasNoCycle()
    {
        var cycle = ContractsGraph.FindCycle(Solution.Projects);

        cycle.ShouldBeNull("Two modules depending on each other's Contracts are one module in disguise: merge them or "
                           + "invert one dependency with an event.");
    }

    private static IEnumerable<Type> Messages(Project project) =>
        Solution.LoadAssembly(project).GetTypes()
            .Where(t => !t.IsNested && !t.IsDefined(typeof(CompilerGeneratedAttribute))
                        && t.Namespace?.StartsWith("Frappe.", StringComparison.Ordinal) == true
                        && !t.Namespace.EndsWith(".Queries", StringComparison.Ordinal)
                        && t.GetMethod("<Clone>$") is not null);

    [GeneratedRegex(@"^[a-z][a-z0-9]*\.[a-z][a-z0-9_]*\.v[1-9][0-9]*$", RegexOptions.None, matchTimeoutMilliseconds: 1_000)]
    private static partial Regex IdentityShape();
}
