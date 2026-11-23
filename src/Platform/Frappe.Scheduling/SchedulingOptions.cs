using System.ComponentModel.DataAnnotations;

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

    /// <summary>
    /// <c>ConnectionStrings:Scheduler</c>: the connection string of the Quartz job store, with the scheduler's own
    /// role (read from the connection strings section, not from <c>Scheduling</c>). Required, no default; a missing
    /// value fails at startup. Pointing it at another database moves the clustered schedule there; jobs it does not
    /// know are not run.
    /// </summary>
    [Required(ErrorMessage = "Configuration 'ConnectionStrings:Scheduler' is required.")]
    public string? ConnectionString { get; set; }

    /// <summary>
    /// <c>Scheduling:ProvisionSchema</c>: whether the scheduler creates the Quartz tables it misses when it starts.
    /// Default false. Range true or false. Only the Migrator and tests need it: Workers connect with a role that
    /// cannot run DDL, so enabling it elsewhere makes startup fail on the missing privilege.
    /// </summary>
    public bool ProvisionSchema { get; set; }
}
