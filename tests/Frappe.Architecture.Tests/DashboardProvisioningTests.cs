using System.Text.Json;
using Frappe.Notifications.Application.Dispatching;
using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// Every module has a dashboard as code that shows each of its business metrics (OBS-19): the JSON under provisioning/dashboards is what
/// Grafana loads at startup, so a metric no panel queries is a metric nobody watches.
/// </summary>
public sealed class DashboardProvisioningTests
{
    private const string Dashboards = "deploy/observability/grafana/provisioning/dashboards";

    /// <summary>The Prometheus names Mimir gives the module's OTLP instruments: dots to underscores, counters end in <c>_total</c>.</summary>
    public static IEnumerable<string> NotificationsMetricNames()
    {
        yield return Prometheus(NotificationsMetrics.DeliveriesMetric) + "_total";
        yield return Prometheus(ProviderFeedbackMetrics.ProviderEventsMetric) + "_total";
        yield return Prometheus(ProviderFeedbackMetrics.SuppressionsMetric) + "_total";
        yield return Prometheus(PreferenceMetrics.UnsubscribesMetric) + "_total";
        yield return Prometheus(ProviderFeedbackMetrics.TimeToDeliveredMetric) + "_seconds_bucket";
    }

    private static string Prometheus(string otel) => otel.Replace('.', '_');

    private static JsonDocument Notifications() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.FullPath, Dashboards, "notifications.json")));

    [Test]
    [MethodDataSource(nameof(NotificationsMetricNames))]
    public async Task Every_business_metric_of_Notifications_is_in_a_panel_query(string metric)
    {
        // Arrange
        using var dashboard = Notifications();

        // Act
        var queries = dashboard.RootElement.GetProperty("panels").EnumerateArray()
            .Where(panel => panel.TryGetProperty("targets", out _))
            .SelectMany(panel => panel.GetProperty("targets").EnumerateArray())
            .Select(target => target.GetProperty("expr").GetString()!)
            .ToArray();

        // Assert
        await Assert.That(queries.Any(query => query.Contains(metric, StringComparison.Ordinal))).IsTrue().Because($"no panel queries {metric}");
    }

    [Test]
    public async Task The_dashboard_is_provisioned_read_only_from_the_mimir_datasource_under_a_stable_uid()
    {
        // Act
        using var dashboard = Notifications();
        var root = dashboard.RootElement;
        var datasources = root.GetProperty("panels").EnumerateArray()
            .Where(panel => panel.TryGetProperty("datasource", out _))
            .Select(panel => panel.GetProperty("datasource").GetProperty("uid").GetString()!)
            .Distinct()
            .ToArray();
        var provider = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot.FullPath, Dashboards, "dashboards.yaml"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(root.GetProperty("uid").GetString()).IsEqualTo("frappe-notifications");
            await Assert.That(datasources).IsEquivalentTo(["mimir"]);
            await Assert.That(provider).Contains("allowUiUpdates: false").And.Contains("path: /etc/grafana/provisioning/dashboards");
        }
    }
}
