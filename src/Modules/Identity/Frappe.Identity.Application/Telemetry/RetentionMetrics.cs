using System.Diagnostics.Metrics;

namespace Frappe.Identity.Application.Telemetry;

/// <summary>
/// The Retention feature's instrument on meter <c>Frappe.Identity</c> (OBS-01, OBS-02), created once through <see cref="IMeterFactory"/>. The
/// target tag is the kind of data purged, a small fixed set.
/// </summary>
public sealed class RetentionMetrics
{
    /// <summary>
    /// Counter: rows the retention purge deleted, by target (<c>sessions</c>, <c>owners</c>, <c>codes</c> or <c>email_change_undos</c>).
    /// </summary>
    public const string PurgedMetric = "frappe.identity.purged";

    /// <summary>The target tag of <see cref="PurgedMetric"/>: the kind of data purged, a small fixed set.</summary>
    public const string TargetTag = "frappe.target";

    private readonly Counter<long> _purged;

    public RetentionMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _purged = meters.Create(IdentityTelemetry.MeterName).CreateCounter<long>(
            PurgedMetric, unit: "{row}", description: "Rows the retention purge deleted, by target.");
    }

    /// <summary>The purge deleted <paramref name="rows"/> rows of <paramref name="target"/> and committed.</summary>
    public void Purged(string target, long rows) => _purged.Add(rows, new KeyValuePair<string, object?>(TargetTag, target));
}
