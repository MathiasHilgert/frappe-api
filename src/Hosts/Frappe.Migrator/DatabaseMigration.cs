using Frappe.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Migrator;

/// <summary>
/// The one-shot database setup, run before the services start: one <see cref="MigrationRun"/> over the listed modules
/// (the settings validated before connecting, the lock, the roles with the session timeouts of
/// <c>Database:StatementTimeout</c> and <c>Database:IdleInTransactionSessionTimeout</c>, each module's migrations as the owner, the revoke
/// from <c>PUBLIC</c>, the grants). Every step is idempotent, so running it on every deployment is safe. Missing
/// configuration fails before touching the database, naming the key.
/// </summary>
public static partial class DatabaseMigration
{
    /// <summary>Runs the setup of <paramref name="modules"/> once; throws on any failure, with the lock released.</summary>
    public static async Task RunAsync(IConfiguration configuration, IReadOnlyCollection<IDatabaseModule> modules, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(logger);

        var options = Bind(configuration);
        var limits = BindLimits(configuration);

        await using var run = await MigrationRun.StartModulesAsync(options, modules, cancellationToken);
        await run.MigrateAsync(limits, cancellationToken);
        LogMigrated(logger, modules.Count);
    }

    /// <summary>The settings of the run, bound from <c>ConnectionStrings:Owner</c> and <c>Roles</c>; <see cref="MigrationRun"/> validates them before it connects.</summary>
    private static MigrationOptions Bind(IConfiguration configuration)
    {
        var options = new MigrationOptions { OwnerConnectionString = configuration.GetConnectionString(MigrationOptions.OwnerConnectionStringName) };
        configuration.GetSection(MigrationOptions.RolesSection).Bind(options.Roles);
        return options;
    }

    /// <summary>
    /// The session timeouts every login role gets (<see cref="DatabaseOptions"/>, section <c>Database</c>, defaults when
    /// unset), validated like the services validate theirs; an invalid one fails naming the key.
    /// </summary>
    private static DatabaseOptions BindLimits(IConfiguration configuration)
    {
        var limits = new DatabaseOptions();
        configuration.GetSection(DatabaseOptions.Section).Bind(limits);
        var result = new DatabaseOptionsValidator().Validate(Options.DefaultName, limits);
        return result.Failed
            ? throw new OptionsValidationException(Options.DefaultName, typeof(DatabaseOptions), result.Failures)
            : limits;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated {Modules} module database(s)")]
    private static partial void LogMigrated(ILogger logger, int modules);
}
