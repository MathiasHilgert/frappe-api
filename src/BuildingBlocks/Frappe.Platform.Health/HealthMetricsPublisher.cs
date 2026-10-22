using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Platform.Health;

/// <summary>
/// Exports the periodic health report as a gauge (1 healthy, 0.5 degraded, 0 unhealthy) per dependency,
/// so dashboards and alerts see an outage without anyone polling the probe.
/// </summary>
public sealed class HealthMetricsPublisher : IHealthCheckPublisher, IDisposable
{
    public const string MeterName = "Frappe.Health";

    private readonly Meter _meter;
    private readonly ConcurrentDictionary<string, double> _latest = new(StringComparer.Ordinal);

    public HealthMetricsPublisher(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _meter.CreateObservableGauge(
            "frappe.health.dependency.status",
            () => _latest.Select(kv => new Measurement<double>(kv.Value, new KeyValuePair<string, object?>("dependency", kv.Key))),
            description: "1 healthy, 0.5 degraded, 0 unhealthy");
    }

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        foreach (var (name, entry) in report.Entries)
        {
            _latest[name] = entry.Status switch
            {
                HealthStatus.Healthy => 1,
                HealthStatus.Degraded => 0.5,
                _ => 0,
            };
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _meter.Dispose();
}
