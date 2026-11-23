using Frappe.Migrator.Settings;
using Frappe.Persistence;
using Frappe.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Migrator;

/// <summary>
/// The one-shot database setup, run before the Api and the Worker start: for every database participant (the modules and the
/// scheduler's job store) its login roles, its migrations as the owner and the grants of each role, under one lock
/// (<see cref="DatabaseProvisioning"/>). Missing configuration fails before touching the database, naming the key.
/// </summary>
public static class DatabaseMigration
{
    /// <summary>The platform's own participants, which every Migrator sets up even with no module: the scheduler's job store.</summary>
    public static IReadOnlyList<IDatabaseParticipant> PlatformParticipants { get; } = [new SchedulingDatabase()];

    /// <summary>Sets up the platform's databases and those of <paramref name="modules"/>, as <paramref name="configuration"/> says.</summary>
    /// <param name="configuration">The owner's connection string, the role passwords and the session limits.</param>
    /// <param name="modules">The module databases, set up after the platform's.</param>
    /// <param name="logger">Where each finished step is logged.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <exception cref="OptionsValidationException">A setting is missing or invalid; nothing was touched.</exception>
    public static async Task RunAsync(
        IConfiguration configuration,
        IReadOnlyCollection<IDatabaseModule> modules,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(logger);

        IReadOnlyCollection<IDatabaseParticipant> participants = [.. PlatformParticipants, .. modules];
        var options = Bind(configuration, participants);
        var limits = BindLimits(configuration);
        var passwords = options.Roles.ToDictionary(role => role.Key, role => role.Value.Password!, StringComparer.OrdinalIgnoreCase);

        await DatabaseProvisioning.RunAsync(options.OwnerConnectionString!, participants, passwords, limits, logger, cancellationToken);
    }

    /// <summary>
    /// The Migrator's settings, bound and validated; anything missing fails with an <see cref="OptionsValidationException"/> naming
    /// the keys.
    /// </summary>
    private static MigrationOptions Bind(IConfiguration configuration, IReadOnlyCollection<IDatabaseParticipant> participants)
    {
        var options = new MigrationOptions { OwnerConnectionString = configuration.GetConnectionString(MigrationOptions.OwnerConnectionStringName) };
        configuration.GetSection(MigrationOptions.RolesSection).Bind(options.Roles);
        var names = participants.SelectMany(participant => participant.Roles)
            .Select(role => role.ConfigName)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var result = new MigrationOptionsValidator([.. names]).Validate(Options.DefaultName, options);
        return result.Failed
            ? throw new OptionsValidationException(Options.DefaultName, typeof(MigrationOptions), result.Failures)
            : options;
    }

    /// <summary>
    /// The session timeouts every login role gets (<see cref="DatabaseOptions"/>, section <c>Database</c>, defaults when
    /// unset), validated like the Api and the Worker validate theirs; an invalid one fails naming the key.
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
}
