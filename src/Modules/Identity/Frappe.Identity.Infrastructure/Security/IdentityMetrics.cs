using System.Diagnostics.Metrics;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>Counters for identity safeguards that degraded (failed open), so an outage shows on dashboards.</summary>
public sealed class IdentityMetrics
{
    public const string MeterName = "Frappe.Identity";
    public const string BreachCheckUnavailable = "frappe.identity.breach_check.unavailable";
    public const string AttemptLimiterUnavailable = "frappe.identity.attempt_limiter.unavailable";

    private readonly Counter<long> _breachCheckUnavailable;
    private readonly Counter<long> _attemptLimiterUnavailable;

    public IdentityMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _breachCheckUnavailable = meter.CreateCounter<long>(BreachCheckUnavailable,
            description: "Breached-password checks skipped because the breach source failed or was slow.");
        _attemptLimiterUnavailable = meter.CreateCounter<long>(AttemptLimiterUnavailable,
            description: "Attempt-limiter operations allowed through because Valkey was unavailable.");
    }

    public void BreachCheckSkipped() => _breachCheckUnavailable.Add(1);

    public void AttemptLimiterSkipped() => _attemptLimiterUnavailable.Add(1);
}
