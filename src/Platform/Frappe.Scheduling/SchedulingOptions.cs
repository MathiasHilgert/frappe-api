namespace Frappe.Scheduling;

/// <summary>
/// The scheduler's job store: <see cref="ConnectionString"/> from <c>ConnectionStrings:Scheduler</c> (required) and
/// <see cref="ProvisionSchema"/> from <c>Scheduling:ProvisionSchema</c> (default false).
/// </summary>
public sealed class SchedulingOptions
{
    /// <summary>The configuration section the hosts bind; the connection string is <see cref="ConnectionStringName"/>.</summary>
    public const string Section = "Scheduling";

    /// <summary>The name of the scheduler's connection string, <c>ConnectionStrings:Scheduler</c>.</summary>
    public const string ConnectionStringName = "Scheduler";

    /// <summary>The connection string of the job store, with the scheduler's own role.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Whether the scheduler creates the Quartz tables it misses when it starts. Only the Migrator, and tests, need it:
    /// Workers connect with a role that cannot run DDL.
    /// </summary>
    public bool ProvisionSchema { get; set; }
}
