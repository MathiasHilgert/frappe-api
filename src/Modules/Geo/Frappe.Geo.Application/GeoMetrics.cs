using System.Diagnostics.Metrics;

namespace Frappe.Geo.Application;

/// <summary>
/// Geo's business metrics on meter <c>Frappe.Geo</c> (<see cref="MeterName"/>), which every host exports through the
/// platform's <c>Frappe.*</c> wildcard. Created once through <see cref="IMeterFactory"/>. Tags carry country codes,
/// source ids, scopes and outcomes only: never what a user typed, a subdivision or anything personal (S15).
/// </summary>
public sealed class GeoMetrics
{
    /// <summary>The meter every Geo instrument belongs to.</summary>
    public const string MeterName = "Frappe.Geo";

    /// <summary>Counter: sync runs, by source and outcome.</summary>
    public const string SyncRunsMetric = "frappe.geo.sync.runs";

    /// <summary>Counter: imported holidays a sync changed under a manual override (the override still wins), by source.</summary>
    public const string OverrideConflictsMetric = "frappe.geo.sync.override_conflicts";

    /// <summary>Observable gauge: seconds since a source's last successful run (applied or unchanged), by source.</summary>
    public const string LastSuccessAgeMetric = "frappe.geo.sync.last_success.age";

    /// <summary>A source's id, for example <c>ar-official-holidays</c>.</summary>
    public const string SourceTag = "frappe.geo.source";

    /// <summary>How a sync run ended: <c>applied</c>, <c>unchanged</c>, <c>not_published</c>, <c>refused</c> or <c>failed</c>.</summary>
    public const string OutcomeTag = "frappe.outcome";

    private readonly Counter<long> _syncRuns;
    private readonly Counter<long> _overrideConflicts;

    public GeoMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        Meter = meters.Create(MeterName);
        _syncRuns = Meter.CreateCounter<long>(
            SyncRunsMetric,
            unit: "{run}",
            description: "Reference data sync runs, by source and outcome.");
        _overrideConflicts = Meter.CreateCounter<long>(
            OverrideConflictsMetric,
            unit: "{conflict}",
            description: "Imported holidays a sync changed under a manual override, which still wins.");
    }

    /// <summary>The <c>Frappe.Geo</c> meter, for instruments declared next to the data they observe.</summary>
    public Meter Meter { get; }

    /// <summary>A run of <paramref name="source"/> ended with <paramref name="outcome"/> and <paramref name="conflicts"/> override conflicts.</summary>
    public void SyncRan(string source, string outcome, int conflicts)
    {
        _syncRuns.Add(1, new(SourceTag, source), new(OutcomeTag, outcome));
        if (conflicts > 0)
        {
            _overrideConflicts.Add(conflicts, new KeyValuePair<string, object?>(SourceTag, source));
        }
    }
}
