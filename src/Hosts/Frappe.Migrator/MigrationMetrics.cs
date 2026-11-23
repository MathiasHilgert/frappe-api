using System.Diagnostics.Metrics;

namespace Frappe.Migrator;

/// <summary>
/// Instruments of a Migrator run on meter <c>Frappe.Migrator</c> (<see cref="MeterName"/>), which the host exports through
/// <c>Frappe.*</c>: one run counted by outcome, and how long it took. Created once through <see cref="IMeterFactory"/>.
/// </summary>
public sealed class MigrationMetrics
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Migrator";

    /// <summary>Counter: finished runs, by outcome.</summary>
    public const string RunsMetric = "frappe.migrator.runs";

    /// <summary>Histogram: how long a run took, by outcome.</summary>
    public const string DurationMetric = "frappe.migrator.duration";

    /// <summary>The run's outcome: <see cref="Succeeded"/> or <see cref="Failed"/>.</summary>
    public const string OutcomeTag = "frappe.migrator.outcome";

    /// <summary>The outcome of a run that completed.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The outcome of a run that stopped on an error.</summary>
    public const string Failed = "failed";

    private readonly Counter<long> _runs;
    private readonly Histogram<double> _duration;

    /// <summary>Creates the instruments on <paramref name="meters"/>.</summary>
    public MigrationMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(MeterName);
        _runs = meter.CreateCounter<long>(RunsMetric, unit: "{run}", description: "Migrator runs that finished, by outcome.");
        _duration = meter.CreateHistogram<double>(DurationMetric, unit: "s", description: "How long a Migrator run took, by outcome.");
    }

    internal void Finished(TimeSpan duration, bool succeeded)
    {
        var outcome = new KeyValuePair<string, object?>(OutcomeTag, succeeded ? Succeeded : Failed);
        _runs.Add(1, outcome);
        _duration.Record(duration.TotalSeconds, outcome);
    }
}
