namespace Frappe.Persistence;

/// <summary>
/// The database entry point of a module, implemented in its Infrastructure project and listed by the Migrator host. It
/// is declarative: it names the module's login role and schema and migrates the schema, while the global phases
/// (roles, then every module's migrations, then the revoke and the grants) stay in <see cref="MigrationRun"/>, under
/// one lock.
/// </summary>
public interface IDatabaseModule
{
    /// <summary>The key of the module's password under <c>Roles</c> in the configuration, such as <c>Orders</c>.</summary>
    string Name { get; }

    /// <summary>The Postgres login role the module's application connects with.</summary>
    string Role { get; }

    /// <summary>The schema the role gets data access to and the migrations create.</summary>
    string Schema { get; }

    /// <summary>
    /// Applies the module's pending migrations as the owner role, once <see cref="MigrationRun.EnsureRolesAsync"/> has
    /// created the roles and while the run holds its lock. Idempotent: running it again changes nothing.
    /// </summary>
    Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken);
}
