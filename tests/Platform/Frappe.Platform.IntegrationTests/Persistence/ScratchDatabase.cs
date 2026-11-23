using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// A database of its own on the shared server, for a test that changes what is database-wide (default privileges, the
/// migration lock key's sessions, roles): dropped, with the roles the test names, when it ends.
/// </summary>
internal static class ScratchDatabase
{
    /// <summary>
    /// Runs <paramref name="test"/> with the owner's connection string of a new database; <paramref name="roles"/> are dropped
    /// afterwards.
    /// </summary>
    public static async Task RunAsync(PostgresServer server, string[] roles, Func<string, Task> test)
    {
        var name = "scratch_" + Guid.NewGuid().ToString("N")[..12];
        var admin = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Pooling = false }.ConnectionString;
        await ExecuteAsync(admin, $"CREATE DATABASE {name}");
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

    public static async Task ExecuteAsync(string connectionString, string sql) => await ScalarAsync(connectionString, sql);

    public static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
