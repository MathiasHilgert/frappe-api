namespace Frappe.Identity.Application.Telemetry;

/// <summary>
/// What the Identity module's metrics classes share (OBS-01, OBS-02): the meter <c>Frappe.Identity</c> they all create their instruments on, and
/// the tag every feature uses. Each feature declares its own instrument names and tags in its <c>&lt;Feature&gt;Metrics</c>.
/// </summary>
public static class IdentityTelemetry
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Identity";

    /// <summary>The outcome tag every module uses (OBS-04).</summary>
    public const string OutcomeTag = "frappe.outcome";
}
