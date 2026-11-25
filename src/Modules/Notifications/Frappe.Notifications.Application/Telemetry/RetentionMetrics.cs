using System.Diagnostics.Metrics;

namespace Frappe.Notifications.Application.Telemetry;

/// <summary>
/// The Retention feature's instruments on meter <c>Frappe.Notifications</c> (OBS-01, OBS-02), created once through <see cref="IMeterFactory"/>.
/// The table tag is a table of the module, a closed set; the outcome of a run is <c>succeeded</c> or <c>failed</c>.
/// </summary>
public sealed class RetentionMetrics
{
    /// <summary>Counter: rows the module's own purge deleted, by table.</summary>
    public const string PurgedMetric = "frappe.notifications.purged";

    /// <summary>Counter: runs of the module's nightly purge, by outcome (<c>succeeded</c> or <c>failed</c>).</summary>
    public const string PurgeRunsMetric = "frappe.notifications.purge.runs";

    /// <summary>The table tag of <see cref="PurgedMetric"/>: a table of the module, a closed set.</summary>
    public const string TableTag = "table";

    private readonly Counter<long> _purged;
    private readonly Counter<long> _purgeRuns;

    public RetentionMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(NotificationsTelemetry.MeterName);
        _purged = meter.CreateCounter<long>(
            PurgedMetric, unit: "{row}", description: "Rows the Notifications purge deleted, by table.");
        _purgeRuns = meter.CreateCounter<long>(
            PurgeRunsMetric, unit: "{run}", description: "Runs of the Notifications nightly purge, by outcome (succeeded or failed).");
    }

    /// <summary>A purge statement series deleted <paramref name="rows"/> rows of <paramref name="table"/>; each statement had committed.</summary>
    public void Purged(string table, long rows) => _purged.Add(rows, new KeyValuePair<string, object?>(TableTag, table));

    /// <summary>The nightly purge ended: every step finished or at least one failed.</summary>
    public void PurgeRun(bool succeeded) =>
        _purgeRuns.Add(1, new KeyValuePair<string, object?>(NotificationsTelemetry.OutcomeTag, succeeded ? "succeeded" : "failed"));
}
