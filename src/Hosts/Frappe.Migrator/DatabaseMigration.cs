using Frappe.Persistence;
using Frappe.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Frappe.Migrator;

/// <summary>
/// The one-shot database setup, run before the Api and the Worker start: module roles, migrations as the owner, the
/// Quartz tables, and data-only grants for each role on its own schema. Every step is idempotent, so running it on
/// every deployment is safe. Missing configuration fails before touching the database, naming the key.
/// </summary>
public static partial class DatabaseMigration
{
    /// <summary>Each module's login role, its schema, and its name under <c>Roles</c> in the configuration.</summary>
    private static readonly (string Role, string Schema, string Name)[] ModuleRoleSettings =
    [
        (SchedulingRegistration.Role, SchedulingRegistration.Schema, "Scheduler"),
    ];

    public static async Task RunAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        var options = Bind(configuration);
        var owner = options.OwnerConnectionString!;
        var roles = ModuleRoleSettings.Select(r => (r.Role, r.Schema, Password: options.Roles[r.Name].Password!)).ToArray();

        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync(cancellationToken);

        // Two Migrators started together (two replicas of a deployment) run one after the other, never interleaved.
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection))
        {
            lockCommand.Parameters.AddWithValue("key", MigrationLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            foreach (var (role, _, password) in roles)
            {
                await ModuleRoles.EnsureLoginRoleAsync(connection, role, password, cancellationToken);
                LogRoleReady(logger, role);
            }

            await SchedulingRegistration.ProvisionAsync(owner, cancellationToken);
            LogMigrated(logger, SchedulingRegistration.Schema);

            foreach (var (role, schema, _) in roles)
            {
                await ModuleRoles.GrantDataAccessAsync(connection, role, schema, cancellationToken);
                LogGranted(logger, role, schema);
            }
        }
        finally
        {
            // Explicit: a pooled connection keeps its session, so closing it would not release the lock right away.
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            unlock.Parameters.AddWithValue("key", MigrationLockKey);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>Session-level advisory lock held for the whole run.</summary>
    private const long MigrationLockKey = 0x46524150504531; // "FRAPPE1"

    /// <summary>The Migrator's settings, bound and validated; anything missing fails with an <see cref="OptionsValidationException"/> naming the keys.</summary>
    private static MigrationOptions Bind(IConfiguration configuration)
    {
        var options = new MigrationOptions { OwnerConnectionString = configuration.GetConnectionString(MigrationOptions.OwnerConnectionStringName) };
        configuration.GetSection(MigrationOptions.RolesSection).Bind(options.Roles);
        var result = new MigrationOptionsValidator(ModuleRoleSettings.Select(r => r.Name).ToArray()).Validate(Options.DefaultName, options);
        return result.Failed
            ? throw new OptionsValidationException(Options.DefaultName, typeof(MigrationOptions), result.Failures)
            : options;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Role {Role} is ready")]
    private static partial void LogRoleReady(ILogger logger, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema {Schema} is migrated")]
    private static partial void LogMigrated(ILogger logger, string schema);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role {Role} can read and write the data of schema {Schema}")]
    private static partial void LogGranted(ILogger logger, string role, string schema);
}
