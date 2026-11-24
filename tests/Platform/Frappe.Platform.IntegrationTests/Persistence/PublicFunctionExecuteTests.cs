using Frappe.Persistence;
using Frappe.Testing.Containers;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// Postgres lets <c>PUBLIC</c>, so every role, execute a new function. The Migrator takes that back database-wide once every
/// module has migrated (MIG-12), so a function a migration creates reaches a role only through an explicit grant.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class PublicFunctionExecuteTests(PostgresServer server)
{
    private const string Password = "public-execute-test-password";

    [Test]
    public async Task A_function_the_owner_creates_afterwards_is_not_callable_by_another_role_until_granted()
    {
        await WithDatabaseAsync(async (owner, other) =>
        {
            // Arrange
            await RevokeAsync(owner);
            await ExecuteAsync(owner, "CREATE FUNCTION public.answer() RETURNS integer LANGUAGE sql AS 'SELECT 42'");

            // Act
            var refused = await Assert.ThrowsAsync<PostgresException>(async () => await ExecuteAsync(other, "SELECT public.answer()"));
            await ExecuteAsync(owner, "GRANT EXECUTE ON FUNCTION public.answer() TO " + RoleOf(other));
            var granted = await ScalarAsync(other, "SELECT public.answer()");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(refused!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(granted).IsEqualTo(42);
            }
        });
    }

    [Test]
    public async Task Running_it_again_changes_nothing()
    {
        await WithDatabaseAsync(async (owner, _) =>
        {
            // Arrange
            await RevokeAsync(owner);

            // Act
            await RevokeAsync(owner);
            await ExecuteAsync(owner, "CREATE FUNCTION public.answer() RETURNS integer LANGUAGE sql AS 'SELECT 42'");
            var publicCanExecute = (bool)(await ScalarAsync(owner, "SELECT has_function_privilege('public', 'public.answer()', 'EXECUTE')"))!;

            // Assert
            await Assert.That(publicCanExecute).IsFalse();
        });
    }

    private static async Task RevokeAsync(string owner)
    {
        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync();
        await ModuleRoles.RevokePublicFunctionExecuteAsync(connection, CancellationToken.None);
    }

    private static string RoleOf(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString).Username!;

    /// <summary>A database of its own (default privileges are per database), the owner's and a plain role's connection strings.</summary>
    private async Task WithDatabaseAsync(Func<string, string, Task> test)
    {
        var name = "public_execute_" + Guid.NewGuid().ToString("N")[..12];
        var role = "pe_" + Guid.NewGuid().ToString("N")[..12];
        var admin = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Pooling = false };
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE {name}");
        try
        {
            var owner = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name }.ConnectionString;
            await using (var connection = new NpgsqlConnection(owner))
            {
                await connection.OpenAsync();
                await ModuleRoles.EnsureLoginRoleAsync(connection, role, Password, CancellationToken.None);
            }

            var other = new NpgsqlConnectionStringBuilder(owner) { Username = role, Password = Password }.ConnectionString;
            await test(owner, other);
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
            await ExecuteAsync(admin.ConnectionString, $"DROP ROLE IF EXISTS {role}");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql) => await ScalarAsync(connectionString, sql);

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
