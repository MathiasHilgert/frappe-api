using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Frappe.Notifications.Application.Dispatching;

/// <summary>
/// The Notifications module's instruments on meter <c>Frappe.Notifications</c> (OBS-01, OBS-02), created once through
/// <see cref="IMeterFactory"/>. Tags are bounded: the kind comes from the catalog, the channel from the registered plug-ins.
/// </summary>
public sealed class NotificationsMetrics
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Notifications";

    /// <summary>Counter: intended deliveries, by kind, channel and outcome.</summary>
    public const string DeliveriesMetric = "frappe.notifications.deliveries";

    /// <summary>Counter: rows the module's own purge deleted, by table.</summary>
    public const string PurgedMetric = "frappe.notifications.purged";

    /// <summary>Counter: runs of the module's nightly purge, by outcome (<c>succeeded</c> or <c>failed</c>).</summary>
    public const string PurgeRunsMetric = "frappe.notifications.purge.runs";

    public const string KindTag = "kind";

    /// <summary>The table tag of <see cref="PurgedMetric"/>: a table of the module, a closed set.</summary>
    public const string TableTag = "table";

    public const string ChannelTag = "channel";

    /// <summary>The outcome tag every module uses (OBS-04).</summary>
    public const string OutcomeTag = "frappe.outcome";

    private readonly Counter<long> _deliveries;
    private readonly Counter<long> _purged;
    private readonly Counter<long> _purgeRuns;

    public NotificationsMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(MeterName);
        _deliveries = meter.CreateCounter<long>(
            DeliveriesMetric, unit: "{delivery}", description: "Intended notification deliveries, by kind, channel and outcome.");
        _purged = meter.CreateCounter<long>(
            PurgedMetric, unit: "{row}", description: "Rows the Notifications purge deleted, by table.");
        _purgeRuns = meter.CreateCounter<long>(
            PurgeRunsMetric, unit: "{run}", description: "Runs of the Notifications nightly purge, by outcome (succeeded or failed).");
    }

    /// <summary>A purge statement series deleted <paramref name="rows"/> rows of <paramref name="table"/>; each statement had committed.</summary>
    public void Purged(string table, long rows) => _purged.Add(rows, new KeyValuePair<string, object?>(TableTag, table));

    /// <summary>The nightly purge ended: every step finished or at least one failed.</summary>
    public void PurgeRun(bool succeeded) =>
        _purgeRuns.Add(1, new KeyValuePair<string, object?>(OutcomeTag, succeeded ? "succeeded" : "failed"));

    internal void Delivery(string kind, string channel, DispatchOutcome outcome) =>
        _deliveries.Add(1, new(KindTag, kind), new(ChannelTag, channel), new(OutcomeTag, OutcomeName(outcome)));

    /// <summary>The tag value of an outcome; each is named, none falls back (OBS-05).</summary>
    internal static string OutcomeName(DispatchOutcome outcome) => outcome switch
    {
        DispatchOutcome.Sent => "sent",
        DispatchOutcome.Failed => "failed",
        DispatchOutcome.Unresolved => "unresolved",
        DispatchOutcome.OptedOut => "opted_out",
        DispatchOutcome.Suppressed => "suppressed",
        DispatchOutcome.Duplicate => "duplicate",
        DispatchOutcome.NoChannel => "no_channel",
        _ => throw new UnreachableException($"Unknown outcome {outcome}."),
    };
}
