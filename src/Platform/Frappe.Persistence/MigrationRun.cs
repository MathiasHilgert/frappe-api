using Microsoft.Extensions.Options;
using Npgsql;

namespace Frappe.Persistence;

/// <summary>
/// One run of the database setup, as the owner role: it holds a database-wide lock for its whole life, so two Migrators
/// started together (two replicas of a deployment) run one after the other, never interleaved. The host drives the
/// phases in order: <see cref="EnsureRolesAsync"/>, its own migrations on <see cref="Connection"/> or the owner connection
/// string, then <see cref="GrantAsync"/>. Every phase is idempotent, so running it on every deployment is safe.
/// </summary>
public sealed class MigrationRun : IAsyncDisposable
{
    /// <summary>Session-level advisory lock held for the whole run.</summary>
    public const long LockKey = 0x46524150504531; // "FRAPPE1"

    /// <summary>How long a run waits before trying the lock again while another one holds it.</summary>
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly MigrationOptions _options;
    private readonly IReadOnlyCollection<ModuleRoleGrant> _roles;
    private readonly IReadOnlyCollection<IDatabaseModule> _modules;

    private MigrationRun(NpgsqlConnection connection, MigrationOptions options, IReadOnlyCollection<ModuleRoleGrant> roles, IReadOnlyCollection<IDatabaseModule> modules)
    {
        Connection = connection;
        _options = options;
        _roles = roles;
        _modules = modules;
    }

    /// <summary>The owner's open connection, which holds the run's lock.</summary>
    public NpgsqlConnection Connection { get; }

    /// <summary>
    /// Validates <paramref name="options"/> (the owner's connection string and a password for every role), and only then
    /// connects and takes the lock. A missing setting fails with an <see cref="OptionsValidationException"/> naming each
    /// key, before the database is touched. Waits while another run holds the lock.
    /// </summary>
    public static Task<MigrationRun> StartAsync(MigrationOptions options, IReadOnlyCollection<ModuleRoleGrant> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return StartAsync(options, roles, [], cancellationToken);
    }

    /// <summary>
    /// Starts a run for the modules a host lists: their roles come from <see cref="IDatabaseModule"/>, and
    /// <see cref="MigrateAsync"/> runs the whole sequence. Validates and locks as
    /// <see cref="StartAsync(MigrationOptions, IReadOnlyCollection{ModuleRoleGrant}, CancellationToken)"/> does, and first
    /// refuses, with an <see cref="ArgumentException"/> naming the value, two modules that declare the same name (one
    /// password key), role (one login for two modules) or schema (one module's data open to the other).
    /// </summary>
    public static Task<MigrationRun> StartModulesAsync(MigrationOptions options, IReadOnlyCollection<IDatabaseModule> modules, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modules);
        RefuseShared(modules, "name", module => module.Name);
        RefuseShared(modules, "role", module => module.Role);
        RefuseShared(modules, "schema", module => module.Schema);
        return StartAsync(options, [.. modules.Select(m => new ModuleRoleGrant(m.Name, m.Role, m.Schema))], modules, cancellationToken);
    }

    private static async Task<MigrationRun> StartAsync(MigrationOptions options, IReadOnlyCollection<ModuleRoleGrant> roles, IReadOnlyCollection<IDatabaseModule> modules, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var validation = new MigrationOptionsValidator([.. roles.Select(r => r.Name)]).Validate(Options.DefaultName, options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(MigrationOptions), validation.Failures);
        }

        var connection = new NpgsqlConnection(options.OwnerConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await AcquireLockAsync(connection, cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return new MigrationRun(connection, options, roles, modules);
    }

    /// <summary>
    /// Role phase: creates every login role (or rotates its password) and gives it the session timeouts of
    /// <paramref name="limits"/>. Run it before the migrations.
    /// </summary>
    public async Task EnsureRolesAsync(DatabaseOptions limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        foreach (var role in _roles)
        {
            await ModuleRoles.EnsureLoginRoleAsync(Connection, role.Role, _options.Roles[role.Name].Password!, cancellationToken);
            await ModuleRoles.LimitSessionsAsync(Connection, role.Role, limits, cancellationToken);
        }
    }

    /// <summary>
    /// The whole setup of a run started by <see cref="StartModulesAsync"/>, in order: the roles, each module's migrations
    /// (in the order listed, as the owner), then the revoke and the grants. A module that fails stops the run before any
    /// grant is given. A run started by <c>StartAsync</c> with roles only has no modules, so there this only ensures the
    /// roles and gives the grants. Every phase is idempotent, so running it on every deployment is safe.
    /// </summary>
    public async Task MigrateAsync(DatabaseOptions limits, CancellationToken cancellationToken)
    {
        await EnsureRolesAsync(limits, cancellationToken);
        foreach (var module in _modules)
        {
            await module.MigrateAsync(_options.OwnerConnectionString!, cancellationToken);
        }

        await GrantAsync(cancellationToken);
    }

    /// <summary>
    /// Grant phase: closes new functions to <c>PUBLIC</c>, then gives every role data access to its own schema. Run it
    /// after every module migrated, never before: an extension a migration installs creates its functions then, and
    /// they must stay callable by <c>PUBLIC</c>.
    /// </summary>
    public async Task GrantAsync(CancellationToken cancellationToken)
    {
        await ModuleRoles.RevokePublicFunctionExecuteAsync(Connection, cancellationToken);
        foreach (var role in _roles)
        {
            await ModuleRoles.GrantDataAccessAsync(Connection, role.Role, role.Schema, cancellationToken);
        }
    }

    /// <summary>
    /// Releases the lock explicitly (a pooled connection keeps its session, so closing it would not) and closes the
    /// connection. A release that fails (the session is already gone, or the connection broke) never throws: disposal
    /// runs while the run's own exception unwinds, and must not replace it. The pool is cleared instead, so the
    /// connection is closed for good rather than returned, and its session, with the lock, ends.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", Connection);
            unlock.Parameters.AddWithValue("key", LockKey);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            NpgsqlConnection.ClearPool(Connection);
        }
        finally
        {
            await Connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Throws when two of <paramref name="modules"/> declare the same <paramref name="kind"/>, ignoring case: the
    /// configuration reads <c>Roles:Orders</c> and <c>Roles:orders</c> as one key.
    /// </summary>
    private static void RefuseShared(IReadOnlyCollection<IDatabaseModule> modules, string kind, Func<IDatabaseModule, string> value)
    {
        var shared = modules.GroupBy(value, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (shared is not null)
        {
            throw new ArgumentException($"More than one module declares the {kind} '{shared.Key}'; every module needs its own.", nameof(modules));
        }
    }

    /// <summary>
    /// Takes the lock by trying it until it is free, never by blocking on it. A session blocked inside
    /// <c>pg_advisory_lock</c> keeps a transaction open while it waits, and a migration that builds an index
    /// <c>CONCURRENTLY</c> waits for every open transaction to end: the run holding the lock and the one waiting for
    /// it would wait for each other forever. Between tries this session holds no transaction.
    /// </summary>
    private static async Task AcquireLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", LockKey);
        while (await command.ExecuteScalarAsync(cancellationToken) is not true)
        {
            await Task.Delay(LockRetryDelay, cancellationToken);
        }
    }
}
