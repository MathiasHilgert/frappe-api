using System.Diagnostics.Metrics;

namespace Frappe.Persistence;

/// <summary>
/// Instruments of the persistence platform on meter <c>Frappe.Persistence</c> (<see cref="MeterName"/>), which hosts export
/// through <c>Frappe.*</c>: what the unit of work could not do after a commit or refused at one, and how long a Migrator
/// waited for the migration lock. Created once through <see cref="IMeterFactory"/>. Tags are module names and a closed
/// set of reasons, never ids or exception messages.
/// </summary>
public sealed class PersistenceMetrics
{
    /// <summary>The meter's name.</summary>
    public const string MeterName = "Frappe.Persistence";

    /// <summary>Counter: after-commit steps that failed or could not be built after their unit committed, by module and reason.</summary>
    public const string AfterCommitFailedMetric = "frappe.persistence.after_commit.failed";

    /// <summary>Counter: commits the storage refused that a module's rule turned into its rejection, by module.</summary>
    public const string CommitRejectedMetric = "frappe.persistence.commit.rejected";

    /// <summary>Counter: commits whose repairs kept failing until the repair limit and ended as a conflict, by module.</summary>
    public const string RepairsExhaustedMetric = "frappe.persistence.commit.repairs_exhausted";

    /// <summary>Histogram: how long a Migrator waited to take the migration lock.</summary>
    public const string MigrationLockWaitMetric = "frappe.persistence.migration_lock.wait";

    /// <summary>The module label, for example <c>orders</c>.</summary>
    public const string ModuleTag = "frappe.module";

    /// <summary>Why an after-commit step did not run: <see cref="StepFailed"/> or <see cref="StepsUnresolved"/>.</summary>
    public const string ReasonTag = "frappe.persistence.reason";

    /// <summary>An after-commit handler threw.</summary>
    public const string StepFailed = "step_failed";

    /// <summary>The container could not build the after-commit handlers of an event, so none ran.</summary>
    public const string StepsUnresolved = "steps_unresolved";

    private readonly Counter<long> _afterCommitFailed;
    private readonly Counter<long> _commitRejected;
    private readonly Counter<long> _repairsExhausted;
    private readonly Histogram<double> _migrationLockWait;

    /// <summary>Creates the instruments on <see cref="MeterName"/>.</summary>
    public PersistenceMetrics(IMeterFactory meterFactory)
        : this(Create(meterFactory))
    {
    }

    private PersistenceMetrics(Meter meter)
    {
        _afterCommitFailed = meter.CreateCounter<long>(
            AfterCommitFailedMetric,
            unit: "{failure}",
            description: "After-commit steps that failed or could not be built after their unit committed, by module and reason.");
        _commitRejected = meter.CreateCounter<long>(
            CommitRejectedMetric,
            unit: "{rejection}",
            description: "Commits the storage refused and a module rule turned into its rejection, by module.");
        _repairsExhausted = meter.CreateCounter<long>(
            RepairsExhaustedMetric,
            unit: "{commit}",
            description: "Commits that kept failing after the repair limit and ended as a conflict, by module.");
        _migrationLockWait = meter.CreateHistogram<double>(
            MigrationLockWaitMetric,
            unit: "s",
            description: "How long a Migrator waited to take the migration lock.");
    }

    /// <summary>
    /// The instruments for code no container builds (the Migrator's provisioning run); hosts still export them, they
    /// subscribe to the meter by name.
    /// </summary>
    internal static PersistenceMetrics Standalone { get; } = new(new Meter(MeterName));

    internal void AfterCommitFailed(string module, string reason) =>
        _afterCommitFailed.Add(1, new KeyValuePair<string, object?>(ModuleTag, module), new KeyValuePair<string, object?>(ReasonTag, reason));

    internal void CommitRejected(string module) => _commitRejected.Add(1, new KeyValuePair<string, object?>(ModuleTag, module));

    internal void RepairsExhausted(string module) => _repairsExhausted.Add(1, new KeyValuePair<string, object?>(ModuleTag, module));

    internal void MigrationLockWaited(TimeSpan waited) => _migrationLockWait.Record(waited.TotalSeconds);

    private static Meter Create(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        return meterFactory.Create(MeterName);
    }
}
