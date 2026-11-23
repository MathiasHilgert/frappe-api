namespace Frappe.Persistence;

/// <summary>
/// Anything that owns a schema the Migrator sets up: its login roles, its migrations and, afterwards, the grants of each
/// role. Modules and platform capabilities (the scheduler's job store) are both participants, so the Migrator runs the same
/// phases for each (<see cref="DatabaseProvisioning"/>) without knowing which is which.
/// </summary>
public interface IDatabaseParticipant
{
    /// <summary>The participant's name, for the Migrator's log.</summary>
    string Name { get; }

    /// <summary>The Postgres schema the participant owns.</summary>
    string Schema { get; }

    /// <summary>The login roles the participant needs; the Migrator creates them before it migrates anything.</summary>
    IReadOnlyList<DatabaseRole> Roles { get; }

    /// <summary>
    /// Brings the participant's schema up to date as the owner. Runs on every Migrator run under the migration lock, so it
    /// must be idempotent.
    /// </summary>
    /// <param name="ownerConnectionString">The owner role's connection string.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken);
}
