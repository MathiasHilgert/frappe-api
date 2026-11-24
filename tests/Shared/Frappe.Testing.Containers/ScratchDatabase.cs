using Npgsql;

namespace Frappe.Testing.Containers;

/// <summary>
/// A database of its own on the shared server, for a test that changes what is database-wide (default privileges, the
/// migration lock key's sessions, roles): dropped, with the roles the test names, when it ends.
/// </summary>
public static class ScratchDatabase
{
    /// <summary>
    /// Runs <paramref name="test"/> with the owner's connection string of a new empty database; <paramref name="roles"/> are
    /// dropped afterwards, so they must be unique to the test and never roles a
    /// <see cref="TemplateDatabase"/> keeps for the session (a role granted inside a template cannot be dropped).
    /// </summary>
    public static Task RunAsync(PostgresServer server, string[] roles, Func<string, Task> test) => RunAsync(server, roles, null, test);

    /// <summary>
    /// Like <see cref="RunAsync(PostgresServer, string[], Func{string, Task})"/>, but the database is a clone of the template
    /// database <paramref name="template"/> (see <see cref="TemplateDatabase"/>), which must have no open connection.
    /// </summary>
    internal static async Task RunAsync(PostgresServer server, string[] roles, string? template, Func<string, Task> test)
    {
        var name = Unique("scratch");
        var admin = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Pooling = false }.ConnectionString;
        await ExecuteAsync(admin, template is null ? $"CREATE DATABASE {name}" : $"CREATE DATABASE {name} TEMPLATE {template}");
        try
        {
            await test(new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString);
        }
        finally
        {
            await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
            foreach (var role in roles)
            {
                await ExecuteAsync(admin, $"DROP ROLE IF EXISTS {role}");
            }
        }
    }

    /// <summary>A name unique to one test, safe as a Postgres identifier.</summary>
    public static string Unique(string prefix) => prefix + "_" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>The connection string of <paramref name="role"/> into the database <paramref name="owner"/> reaches.</summary>
    public static string LoginOf(string owner, string role, string password) =>
        new NpgsqlConnectionStringBuilder(owner) { Username = role, Password = password, Pooling = false }.ConnectionString;

    /// <summary>Runs <paramref name="sql"/> on <paramref name="connectionString"/> and discards the result.</summary>
    public static async Task ExecuteAsync(string connectionString, string sql) => await ScalarAsync(connectionString, sql);

    /// <summary>Runs <paramref name="sql"/> on <paramref name="connectionString"/> and returns its first value.</summary>
    public static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
