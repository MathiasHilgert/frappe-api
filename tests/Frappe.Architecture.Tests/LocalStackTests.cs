using System.Text.RegularExpressions;
using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The local stack (<c>deploy/</c>) starts the platform, the hosts and the observability services, with Grafana provisioned with its
/// datasources only, nothing a module owns, and nothing in the committed example that is not obviously local.
/// </summary>
public sealed partial class LocalStackTests
{
    private static readonly string[] Services =
    [
        "postgres", "nats", "valkey", "migrator", "api", "worker", "otel-collector", "loki", "tempo", "mimir", "grafana",
    ];

    private static string Deploy(string relative) => Path.Combine(RepositoryRoot.FullPath, "deploy", relative);

    [GeneratedRegex(@"^  ([a-z][a-z0-9-]*):\s*$", RegexOptions.Multiline)]
    private static partial Regex ServiceName();

    [GeneratedRegex(@"\$\{([A-Z][A-Z0-9_]*):\?", RegexOptions.None)]
    private static partial Regex RequiredVariable();

    [GeneratedRegex(@"^([A-Z][A-Z0-9_]*)=(.*)$", RegexOptions.Multiline)]
    private static partial Regex Assignment();

    /// <summary>
    /// A placeholder says it is local by starting with <c>local-only-</c>, or, for a value that must be base64 (a key), by decoding to text
    /// that does.
    /// </summary>
    private static bool SaysLocal(string value)
    {
        if (value.StartsWith("local-only-", StringComparison.Ordinal))
        {
            return true;
        }

        var bytes = new byte[value.Length];
        return Convert.TryFromBase64String(value, bytes, out var written)
            && System.Text.Encoding.UTF8.GetString(bytes, 0, written).StartsWith("local-only-", StringComparison.Ordinal);
    }

    private static async Task<string> ComposeServicesAsync()
    {
        var compose = await File.ReadAllTextAsync(Deploy("compose.yaml"));
        var start = compose.IndexOf("\nservices:\n", StringComparison.Ordinal);
        var end = compose.IndexOf("\nvolumes:\n", StringComparison.Ordinal);
        return compose[start..end];
    }

    [Test]
    public async Task The_stack_starts_the_platform_the_hosts_and_the_observability_services_and_nothing_module_specific()
    {
        // Act
        var services = ServiceName().Matches(await ComposeServicesAsync()).Select(match => match.Groups[1].Value).ToArray();

        // Assert
        await Assert.That(services).IsEquivalentTo(Services);
    }

    [Test]
    public async Task Grafana_is_provisioned_with_its_datasources_and_no_dashboard_or_alert()
    {
        // Act
        var provisioning = Deploy("observability/grafana/provisioning");
        var folders = Directory.EnumerateDirectories(provisioning).Select(path => Path.GetFileName(path)).ToArray();
        var files = Directory.EnumerateFiles(provisioning, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(provisioning, path))
            .ToArray();
        var datasources = await File.ReadAllTextAsync(Path.Combine(provisioning, "datasources", "datasources.yaml"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(folders).IsEquivalentTo(["datasources"]);
            await Assert.That(files).IsEquivalentTo([Path.Combine("datasources", "datasources.yaml")]);
            await Assert.That(datasources).Contains("uid: mimir").And.Contains("uid: loki").And.Contains("uid: tempo");
        }
    }

    [Test]
    public async Task Every_variable_compose_requires_is_set_in_the_example_so_copying_it_starts_the_stack()
    {
        // Act
        var required = RequiredVariable().Matches(await File.ReadAllTextAsync(Deploy("compose.yaml")))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
        var example = Assignment().Matches(await File.ReadAllTextAsync(Deploy(".env.example"))).Select(match => match.Groups[1].Value).ToArray();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(required).IsNotEmpty();
            await Assert.That(required.Except(example)).IsEmpty();
        }
    }

    [Test]
    public async Task The_example_holds_only_obviously_local_secrets()
    {
        // Act
        var secrets = Assignment().Matches(await File.ReadAllTextAsync(Deploy(".env.example")))
            .Select(match => (Name: match.Groups[1].Value, Value: match.Groups[2].Value.Trim()))
            .Where(entry => entry.Name.Contains("PASSWORD", StringComparison.Ordinal) || entry.Name.Contains("KEY", StringComparison.Ordinal)
                || entry.Name.Contains("SECRET", StringComparison.Ordinal) || entry.Name.Contains("TOKEN", StringComparison.Ordinal))
            .ToArray();
        var offenders = secrets
            .Where(entry => entry.Value.Length > 0 && !SaysLocal(entry.Value))
            .Select(entry => entry.Name);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(secrets).IsNotEmpty();
            await Assert.That(offenders).IsEmpty().Because("S16: a committed secret is a placeholder that says it is local");
        }
    }
}
