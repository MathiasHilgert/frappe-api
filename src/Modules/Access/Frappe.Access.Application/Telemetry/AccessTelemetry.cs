namespace Frappe.Access.Application.Telemetry;

/// <summary>
/// What the Access module's metrics classes share (OBS-01, OBS-02): the meter <c>Frappe.Access</c> and the tag every module uses for an outcome.
/// </summary>
public static class AccessTelemetry
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Access";

    /// <summary>The outcome tag every module uses (OBS-04).</summary>
    public const string OutcomeTag = "frappe.outcome";
}
