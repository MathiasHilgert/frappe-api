using System.Diagnostics.Metrics;

namespace Frappe.Identity.Application.Telemetry;

/// <summary>
/// The Limits feature's instrument on meter <c>Frappe.Identity</c> (OBS-01, OBS-02), created once through <see cref="IMeterFactory"/>. The flow
/// tag is the name of a declared limit flow, a small fixed set.
/// </summary>
public sealed class LimitsMetrics
{
    /// <summary>Counter: attempts an attempt limiter could not count because its store was unreachable, by flow.</summary>
    public const string LimiterUnavailableMetric = "frappe.identity.limiter.unavailable";

    /// <summary>The flow tag of <see cref="LimiterUnavailableMetric"/>: the name of a declared limit flow, a small fixed set.</summary>
    public const string FlowTag = "frappe.flow";

    private readonly Counter<long> _limiterUnavailable;

    public LimitsMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _limiterUnavailable = meters.Create(IdentityTelemetry.MeterName).CreateCounter<long>(
            LimiterUnavailableMetric, unit: "{event}",
            description: "Attempts the limiter could not count because its store was unreachable, by flow.");
    }

    /// <summary>An attempt of <paramref name="flow"/> could not be counted: the store was unreachable (its failure mode decided the rest).</summary>
    public void LimiterUnavailable(string flow) => _limiterUnavailable.Add(1, new KeyValuePair<string, object?>(FlowTag, flow));
}
