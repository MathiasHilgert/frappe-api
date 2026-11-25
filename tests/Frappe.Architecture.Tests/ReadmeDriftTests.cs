using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// STR-14: each module's README is checked against its code, so its tables cannot drift. Each test runs its rule on every module and on a drifted
/// fixture, so the rule is proven able to see a breach.
/// </summary>
public sealed class ReadmeDriftTests
{
    private static readonly string[] Modules = ModuleDoorTests.AllModules;

    private static string ModuleFolder(string module) => Path.Combine(RepositoryRoot.FullPath, "src", "Modules", module);

    private static string Readme(string module) => File.ReadAllText(Path.Combine(ModuleFolder(module), "README.md"));

    private static string[] ForEveryModule(Func<string, IEnumerable<string>> offenders) =>
        [.. Modules.SelectMany(module => offenders(module).Select(offense => $"{module}: {offense}"))];

    [Test]
    public async Task Every_relative_link_in_a_module_readme_resolves()
    {
        // Act
        var broken = ForEveryModule(module => ReadmeRules.BrokenLinks(Readme(module), target =>
        {
            var path = Path.GetFullPath(Path.Combine(ModuleFolder(module), target));
            return File.Exists(path) || Directory.Exists(path);
        }));

        // Assert
        await Assert.That(broken).IsEmpty().Because($"STR-14: {string.Join("; ", broken)}");
    }

    [Test]
    public async Task The_link_rule_can_see_a_link_to_a_moved_file()
    {
        // Arrange
        const string readme = "[Here](Kept.cs), [gone](Moved/Old.cs#L3), [web](https://example.com) and [anchor](#metrics).";

        // Act
        var broken = ReadmeRules.BrokenLinks(readme, target => target == "Kept.cs");

        // Assert
        await Assert.That(broken).IsEquivalentTo(["Moved/Old.cs: no such file or folder"]);
    }

    [Test]
    public async Task Every_module_readme_lists_the_instruments_its_code_publishes()
    {
        // Arrange
        var facts = MeterConventionTests.Collect(MeterConventionTests.MetricsTypes);

        // Act
        var drift = ForEveryModule(module => ReadmeRules.MetricDrift(
            Readme(module),
            facts.Where(fact => fact.Meter == $"Frappe.{module}").Select(fact => (fact.Name, fact.Owner.Name))));

        // Assert
        await Assert.That(drift).IsEmpty().Because($"STR-14: {string.Join("; ", drift)}");
    }

    [Test]
    public async Task The_metrics_rule_can_see_a_missing_a_stale_and_a_misattributed_row()
    {
        // Arrange
        const string readme = """
            ## Metrics (meter `Frappe.Identity`)

            | Name | Kind | Unit | Tags | Class |
            |------|------|------|------|-------|
            | `frappe.identity.logins` | Counter | `{login}` | `frappe.outcome` | [OwnersMetrics](Telemetry/OwnersMetrics.cs) |
            | `frappe.identity.removed` | Counter | `{row}` | none | [RetentionMetrics](Telemetry/RetentionMetrics.cs) |

            ## Events
            """;

        // Act
        var drift = ReadmeRules.MetricDrift(
            readme, [("frappe.identity.logins", "SessionsMetrics"), ("frappe.identity.step_ups", "SessionsMetrics")]);

        // Assert
        await Assert.That(drift).IsEquivalentTo(
        [
            "frappe.identity.step_ups: not in the README",
            "frappe.identity.removed: in the README, not in the code",
            "frappe.identity.logins: the README names OwnersMetrics, the code SessionsMetrics",
        ]);
    }

    [Test]
    public async Task Every_module_readme_lists_its_extensions()
    {
        // Act
        var drift = ForEveryModule(module => ReadmeRules.ExtensionDrift(
            Readme(module),
            StructureRules.ModuleProjects()
                .Where(path => path.StartsWith($"{module}/Extensions/", StringComparison.Ordinal))
                .Select(path => Path.GetFileNameWithoutExtension(path))));

        // Assert
        await Assert.That(drift).IsEmpty().Because($"STR-14: {string.Join("; ", drift)}");
    }

    [Test]
    public async Task The_extensions_rule_can_see_an_unlisted_and_a_removed_extension()
    {
        // Arrange
        const string readme = """
            ## Extensions

            | Project | Adds |
            |---------|------|
            | [Messages.Account](Extensions/Frappe.Notifications.Messages.Account) | Account messages |
            | [Kinds.Old](Extensions/Frappe.Notifications.Kinds.Old/) | Gone |
            """;

        // Act
        var drift = ReadmeRules.ExtensionDrift(
            readme, ["Frappe.Notifications.Messages.Account", "Frappe.Notifications.Senders.Email"]);

        // Assert
        await Assert.That(drift).IsEquivalentTo(
        [
            "Frappe.Notifications.Senders.Email: not in the README",
            "Frappe.Notifications.Kinds.Old: in the README, not under Extensions/",
        ]);
    }
}
