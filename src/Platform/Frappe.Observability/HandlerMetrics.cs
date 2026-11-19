using System.Diagnostics.Metrics;

namespace Frappe.Observability;

/// <summary>Holds the platform instruments, created once through <see cref="IMeterFactory"/>.</summary>
public sealed class HandlerMetrics
{
    public HandlerMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(FrappeTelemetry.Name);
        Duration = meter.CreateHistogram<double>(
            FrappeTelemetry.HandlerDurationMetric,
            unit: "s",
            description: "Duration of command and query handlers.");
    }

    internal Histogram<double> Duration { get; }
}
