using Frappe.Persistence;

namespace Frappe.Scheduling;

/// <summary>
/// The scheduler's job store as the Migrator sees it: schema <c>quartz</c>, created with its tables by the owner role, and
/// the login role <c>frappe_scheduler</c> with data access on it. It sets up the same phases as a module's database.
/// </summary>
public sealed class SchedulingDatabase : IDatabaseParticipant
{
    /// <summary>The name of the scheduler's role under <c>Roles</c> in the Migrator's configuration.</summary>
    public const string RoleConfigName = "Scheduler";

    /// <inheritdoc />
    public string Name => "scheduling";

    /// <inheritdoc />
    public string Schema => SchedulingRegistration.Schema;

    /// <inheritdoc />
    public IReadOnlyList<DatabaseRole> Roles { get; } = [new(SchedulingRegistration.Role, RoleConfigName, RoleGrant.Schema)];

    /// <inheritdoc />
    public Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken) =>
        SchedulingRegistration.ProvisionAsync(ownerConnectionString, cancellationToken);
}
