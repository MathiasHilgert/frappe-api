using Microsoft.Extensions.Logging;
using Npgsql;

namespace Frappe.Persistence;

/// <summary>
/// The Migrator's phases over every <see cref="IDatabaseParticipant"/>, under one <see cref="MigrationLock"/>: the login
/// roles (each with the session timeouts of <see cref="DatabaseOptions"/>), the migrations as the owner, the database-wide
/// revoke of <c>PUBLIC</c>'s function execution, and the grants of each role. The phases are global on purpose: a grant
/// must see every participant's tables, and the revoke must come after every extension a migration installs. Every step is
/// idempotent, so running it on every deployment is safe.
/// </summary>
public static partial class DatabaseProvisioning
{
    /// <summary>Sets up the databases of <paramref name="participants"/>, in the order given.</summary>
    /// <param name="ownerConnectionString">The owner role's connection string, which creates roles and runs the migrations.</param>
    /// <param name="participants">What to set up: modules and platform capabilities alike.</param>
    /// <param name="rolePasswords">The password of each role, by <see cref="DatabaseRole.ConfigName"/>.</param>
    /// <param name="limits">The session timeouts every login role gets.</param>
    /// <param name="logger">Where each finished step is logged.</param>
    /// <param name="cancellationToken">Cancels the run; the lock is still released.</param>
    /// <param name="metrics">Where the wait for the migration lock is measured; none uses the platform's own instruments.</param>
    /// <exception cref="InvalidOperationException">A role has no password; nothing was touched.</exception>
    public static async Task RunAsync(
        string ownerConnectionString,
        IReadOnlyCollection<IDatabaseParticipant> participants,
        IReadOnlyDictionary<string, string> rolePasswords,
        DatabaseOptions limits,
        ILogger logger,
        CancellationToken cancellationToken,
        PersistenceMetrics? metrics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerConnectionString);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(rolePasswords);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(logger);

        var passwords = PasswordsOf(participants, rolePasswords);

        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var migrationLock = await MigrationLock.AcquireAsync(connection, cancellationToken, logger, metrics);

        foreach (var role in participants.SelectMany(participant => participant.Roles))
        {
            await ModuleRoles.EnsureLoginRoleAsync(connection, role.Name, passwords[role.ConfigName], cancellationToken);
            await ModuleRoles.LimitSessionsAsync(connection, role.Name, limits, cancellationToken);
            LogRoleReady(logger, role.Name);
        }

        foreach (var participant in participants)
        {
            await participant.MigrateAsync(ownerConnectionString, cancellationToken);
            LogMigrated(logger, participant.Schema);
        }

        // After every participant migrated, never before: an extension a migration installs (pg_trgm) creates its functions
        // then, and they must stay callable by PUBLIC. Every later migration starts under the closed default.
        await ModuleRoles.RevokePublicFunctionExecuteAsync(connection, cancellationToken);

        foreach (var participant in participants)
        {
            await ModuleRoles.RevokePublicDefinerExecuteAsync(connection, participant.Schema, cancellationToken);
            foreach (var role in participant.Roles)
            {
                await role.Grant.ApplyAsync(connection, role.Name, participant.Schema, cancellationToken);
                LogGranted(logger, role.Name, participant.Schema);
            }
        }
    }

    /// <summary>The password of every role, by config name, checked before the database is touched.</summary>
    private static Dictionary<string, string> PasswordsOf(
        IReadOnlyCollection<IDatabaseParticipant> participants, IReadOnlyDictionary<string, string> rolePasswords)
    {
        var passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in participants.SelectMany(participant => participant.Roles))
        {
            if (!rolePasswords.TryGetValue(role.ConfigName, out var password) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException($"Role '{role.Name}' has no password: '{role.ConfigName}' is missing from the role passwords.");
            }

            passwords[role.ConfigName] = password;
        }

        return passwords;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Role {Role} is ready")]
    private static partial void LogRoleReady(ILogger logger, string role);

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema {Schema} is migrated")]
    private static partial void LogMigrated(ILogger logger, string schema);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role {Role} holds its grants on schema {Schema}")]
    private static partial void LogGranted(ILogger logger, string role, string schema);
}
