using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Frappe.Testing.Containers;

/// <summary>
/// Migrates a database once per test session and gives each test a clone of it (<c>CREATE DATABASE ... TEMPLATE ...</c>), so a
/// test owns its database without paying for the migration. The template is marked <c>IS_TEMPLATE</c> and has no open
/// connection when it is cloned. Under <c>FRAPPE_TEST_REUSE=1</c> the template survives while the container keeps running; Postgres data is on tmpfs,
/// so a restarted reused container is empty and every template is rebuilt. Put what the migration depends on into the key
/// (see <see cref="VersionOf"/>); templates of older keys stay until <c>just test reuse-clean</c>.
/// </summary>
public static class TemplateDatabase
{
    private const string NoActiveConnectionsToTemplate = "55006";

    private static readonly System.Text.RegularExpressions.Regex RoleName =
        new("^[a-z_][a-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> Built = [];

    /// <summary>A key fragment that changes whenever <paramref name="assembly"/> is rebuilt (its module version id).</summary>
    public static string VersionOf(System.Reflection.Assembly assembly) => assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>
    /// The name of the template database for <paramref name="key"/>, migrating it first when this session (or, under reuse, an
    /// earlier run) has not. <paramref name="migrate"/> receives the owner's connection string of the new database; concurrent
    /// callers wait for the one migration.
    /// </summary>
    public static async Task<string> GetOrCreateAsync(PostgresServer server, string key, Func<string, Task> migrate)
    {
        var name = NameOf(key);
        var admin = AdminOf(server);
        var identity = server.ConnectionString + "|" + name;
        await Gate.WaitAsync();
        try
        {
            if (Built.Contains(identity))
            {
                return name;
            }

            if (await ScratchDatabase.ScalarAsync(admin, $"SELECT 1 FROM pg_database WHERE datname = '{name}' AND datistemplate") is null)
            {
                await BuildAsync(admin, name, migrate);
            }

            Built.Add(identity);
            return name;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Runs <paramref name="test"/> with the owner's connection string of a database cloned from the template of <paramref name="key"/>
    /// (migrated by <paramref name="migrate"/> once). <paramref name="roles"/> are cluster-wide roles the migration needs: created
    /// <c>NOLOGIN</c> and kept, when missing, before the template is built. They are cluster-wide for the whole session, so
    /// they must not be among the roles a <see cref="ScratchDatabase"/> test drops (a role granted inside a template cannot be
    /// dropped). Role names must match <c>^[a-z_][a-z0-9_]*$</c>; anything else throws <see cref="ArgumentException"/>.
    /// The clone is dropped afterwards.
    /// </summary>
    public static async Task RunAsync(
        PostgresServer server, string key, Func<string, Task> migrate, IReadOnlyCollection<string> roles, Func<string, Task> test)
    {
        var invalid = roles.FirstOrDefault(role => !RoleName.IsMatch(role));
        if (invalid is not null)
        {
            throw new ArgumentException($"Invalid role name '{invalid}'.", nameof(roles));
        }

        foreach (var role in roles)
        {
            await EnsureRoleAsync(AdminOf(server), role);
        }

        var template = await GetOrCreateAsync(server, key, migrate);
        await CloneAsync(server, template, test);
    }

    private static async Task CloneAsync(PostgresServer server, string template, Func<string, Task> test)
    {
        var attempt = 1;
        while (true)
        {
            try
            {
                await ScratchDatabase.RunAsync(server, [], template, test);
                return;
            }
            catch (PostgresException exception) when (exception.SqlState == NoActiveConnectionsToTemplate && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt++));
            }
        }
    }

    private static async Task BuildAsync(string admin, string name, Func<string, Task> migrate)
    {
        await ScratchDatabase.ExecuteAsync(admin, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
        await ScratchDatabase.ExecuteAsync(admin, $"CREATE DATABASE {name}");
        try
        {
            await migrate(new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString);
            NpgsqlConnection.ClearAllPools();
            await ScratchDatabase.ExecuteAsync(admin, $"ALTER DATABASE {name} IS_TEMPLATE true");
        }
        catch
        {
            NpgsqlConnection.ClearAllPools();
            await ScratchDatabase.ExecuteAsync(admin, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
            throw;
        }
    }

    private static async Task EnsureRoleAsync(string admin, string role) =>
        await ScratchDatabase.ExecuteAsync(
            admin,
            $"DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN CREATE ROLE {role} NOLOGIN; END IF; "
            + "EXCEPTION WHEN duplicate_object THEN NULL; END $$");

    private static string AdminOf(PostgresServer server) =>
        new NpgsqlConnectionStringBuilder(server.ConnectionString) { Pooling = false }.ConnectionString;

    private static string NameOf(string key) =>
        "tpl_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
}
