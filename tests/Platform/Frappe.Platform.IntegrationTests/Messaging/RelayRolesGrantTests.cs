using Frappe.Messaging.Storage.Relay;
using Frappe.Platform.Testing.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>The narrow relay role gets exactly what the relay and the purge need, applied to a real schema.</summary>
[ClassDataSource<InboxDatabase>(Shared = SharedType.PerTestSession)]
public sealed class RelayRolesGrantTests(InboxDatabase database)
{
    private static async Task<bool> AskAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task Granting_the_role_lets_it_read_and_delete_the_messages_tables_and_call_the_purge_functions_only()
    {
        // Arrange
        var connectionString = database.NewConnectionString();
        var options = new DbContextOptionsBuilder<InboxTestContext>().UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime()).Options;
        await using (var context = new InboxTestContext(options))
        {
            await context.Database.EnsureCreatedAsync();
        }

        var role = "relay_" + Guid.NewGuid().ToString("N")[..10];
        await using var owner = new NpgsqlConnection(connectionString);
        await owner.OpenAsync();
        await using (var setup = new NpgsqlCommand(
            $"CREATE ROLE {role}; CREATE FUNCTION module.purge_it(timestamptz, integer) RETURNS integer LANGUAGE sql AS 'SELECT 1'", owner))
        {
            await setup.ExecuteNonQueryAsync();
        }

        // Act
        await RelayRoles.GrantAsync(owner, "module", role, ["module.purge_it(timestamptz, integer)"], CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(await AskAsync(owner, $"SELECT has_schema_privilege('{role}', 'module', 'USAGE')")).IsTrue();
            await Assert.That(await AskAsync(owner, $"SELECT has_table_privilege('{role}', 'module.outbox_messages', 'SELECT')")).IsTrue();
            await Assert.That(await AskAsync(owner, $"SELECT has_table_privilege('{role}', 'module.outbox_messages', 'DELETE')")).IsTrue();
            await Assert.That(await AskAsync(owner, $"SELECT has_table_privilege('{role}', 'module.outbox_messages', 'INSERT')")).IsFalse();
            await Assert.That(await AskAsync(owner, $"SELECT has_table_privilege('{role}', 'module.inbox_messages', 'DELETE')")).IsTrue();
            await Assert.That(await AskAsync(owner, $"SELECT has_table_privilege('{role}', 'module.inbox_messages', 'INSERT')")).IsFalse();
            await Assert.That(await AskAsync(owner, $"SELECT has_table_privilege('{role}', 'module.effects', 'SELECT')")).IsFalse();
            await Assert.That(
                await AskAsync(owner, $"SELECT has_function_privilege('{role}', 'module.purge_it(timestamptz, integer)', 'EXECUTE')")).IsTrue();
        }
    }

    [Test]
    public async Task Granting_needs_a_connection()
    {
        // Arrange and Act
        var failure = await Assert.ThrowsAsync<ArgumentNullException>(
            () => RelayRoles.GrantAsync(null!, "module", "relay", [], CancellationToken.None));

        // Assert
        await Assert.That(failure).IsNotNull();
    }
}
