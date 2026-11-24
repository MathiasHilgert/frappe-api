using Frappe.Notifications.Infrastructure.Persistence;
using Npgsql;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// What each Notifications login role may do, as the full table-by-privilege matrix (MIG-11, MIG-12): the Api's role has data access
/// on the schema and no DDL; the Worker's role has the relay's grants plus exactly what its consumers and purge need, and nothing else.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class NotificationsRolesTests(PostgresServer server)
{
    private static readonly string[] Tables =
        ["deliveries", "suppressions", "preferences", "provider_events", "outbox_messages", "inbox_messages", "__ef_migrations_history"];

    private const string Sequence = "notifications.outbox_messages_sequence_seq";

    private static readonly string[] Privileges = ["SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER"];

    private static readonly string[] OutboxRelayColumns = ["published_at", "attempts", "next_attempt_at", "leased_until", "last_error"];

    private static readonly Dictionary<string, string[]> ApiGrants = new()
    {
        ["deliveries"] = ["SELECT", "INSERT", "UPDATE", "DELETE"],
        ["suppressions"] = ["SELECT", "INSERT", "UPDATE", "DELETE"],
        ["preferences"] = ["SELECT", "INSERT", "UPDATE", "DELETE"],
        ["provider_events"] = ["SELECT", "INSERT", "UPDATE", "DELETE"],
        ["outbox_messages"] = ["SELECT", "INSERT", "UPDATE", "DELETE"],
        ["inbox_messages"] = ["SELECT", "INSERT", "UPDATE", "DELETE"],
        ["__ef_migrations_history"] = [],
    };

    private static readonly Dictionary<string, string[]> WorkerGrants = new()
    {
        ["deliveries"] = ["SELECT", "INSERT", "DELETE"],
        ["suppressions"] = ["SELECT"],
        ["preferences"] = ["SELECT"],
        ["provider_events"] = ["DELETE"],
        ["outbox_messages"] = ["SELECT", "DELETE"],
        ["inbox_messages"] = ["SELECT", "INSERT", "DELETE"],
        ["__ef_migrations_history"] = [],
    };

    [Test]
    public async Task The_Api_role_has_data_access_to_every_table_but_the_migration_history()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Act
            var held = await HeldAsync(owner, NotificationsDatabase.ApiRole);

            // Assert
            await Assert.That(held).IsEquivalentTo(Matrix(ApiGrants));
        });
    }

    [Test]
    public async Task The_Worker_role_has_exactly_the_grants_its_relay_consumers_and_purge_need()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Act
            var held = await HeldAsync(owner, NotificationsDatabase.WorkerRole);

            // Assert
            await Assert.That(held).IsEquivalentTo(Matrix(WorkerGrants));
        });
    }

    [Test]
    public async Task The_Worker_role_updates_only_the_outbox_columns_the_relay_writes_and_reads_only_the_purge_column_of_provider_events()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Act
            var outbox = await ColumnPrivilegesAsync(owner, NotificationsDatabase.WorkerRole, "outbox_messages", "UPDATE");
            var events = await ColumnPrivilegesAsync(owner, NotificationsDatabase.WorkerRole, "provider_events", "SELECT");
            var deliveries = await ColumnPrivilegesAsync(owner, NotificationsDatabase.WorkerRole, "deliveries", "UPDATE");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(outbox).IsEquivalentTo(OutboxRelayColumns);
                await Assert.That(events).IsEquivalentTo(["received_at"]);
                await Assert.That(deliveries).IsEmpty();
            }
        });
    }

    [Test]
    public async Task Neither_role_can_create_objects_in_the_schema_or_use_the_outbox_sequence_beyond_its_job()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Act
            var api = ScratchDatabase.LoginOf(owner, NotificationsDatabase.ApiRole, MigratedNotifications.ApiPassword);
            var worker = ScratchDatabase.LoginOf(owner, NotificationsDatabase.WorkerRole, MigratedNotifications.WorkerPassword);
            var apiDdl = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(api, "CREATE TABLE notifications.other (id integer)"));
            var workerDdl = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(worker, "CREATE TABLE notifications.other (id integer)"));
            var workerSequence = (bool)(await ScratchDatabase.ScalarAsync(
                owner, $"SELECT has_sequence_privilege('{NotificationsDatabase.WorkerRole}', '{Sequence}', 'USAGE')"))!;
            var apiSequence = (bool)(await ScratchDatabase.ScalarAsync(
                owner, $"SELECT has_sequence_privilege('{NotificationsDatabase.ApiRole}', '{Sequence}', 'USAGE')"))!;

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(apiDdl!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(workerDdl!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(workerSequence).IsFalse();
                await Assert.That(apiSequence).IsTrue();
            }
        });
    }

    [Test]
    [Arguments("api")]
    [Arguments("worker")]
    public async Task Neither_role_reaches_the_quartz_schema_or_creates_objects_in_public(string which)
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var role = which == "api" ? NotificationsDatabase.ApiRole : NotificationsDatabase.WorkerRole;
            await ScratchDatabase.ExecuteAsync(owner, "CREATE SCHEMA IF NOT EXISTS quartz");

            // Act
            var quartz = (bool)(await ScratchDatabase.ScalarAsync(owner, $"SELECT has_schema_privilege('{role}', 'quartz', 'USAGE')"))!;
            var publicCreate = (bool)(await ScratchDatabase.ScalarAsync(owner, $"SELECT has_schema_privilege('{role}', 'public', 'CREATE')"))!;

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(quartz).IsFalse();
                await Assert.That(publicCreate).IsFalse();
            }
        });
    }

    [Test]
    public async Task The_Worker_purges_old_deliveries_and_provider_events_by_their_dates_and_cannot_read_another_provider_event_column()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await ScratchDatabase.ExecuteAsync(owner, """
                INSERT INTO notifications.deliveries
                  (id, source_message_id, kind, category, channel, recipient_key, status, created_at, status_changed_at)
                VALUES
                  (gen_random_uuid(), gen_random_uuid(), 'account_activation_code', 'security', 'email', 'ref:principal:1', 'unresolved',
                    now() - interval '100 days', now() - interval '100 days'),
                  (gen_random_uuid(), gen_random_uuid(), 'account_activation_code', 'security', 'email', 'ref:principal:2', 'unresolved',
                    now(), now());
                INSERT INTO notifications.provider_events (provider, event_id, received_at, matched)
                VALUES ('resend', 'old', now() - interval '8 days', true), ('resend', 'new', now(), true)
                """);
            var worker = ScratchDatabase.LoginOf(owner, NotificationsDatabase.WorkerRole, MigratedNotifications.WorkerPassword);

            // Act
            await ScratchDatabase.ExecuteAsync(worker, "DELETE FROM notifications.deliveries WHERE created_at < now() - interval '90 days'");
            await ScratchDatabase.ExecuteAsync(worker, "DELETE FROM notifications.provider_events WHERE received_at < now() - interval '7 days'");
            var readsId = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(worker, "SELECT event_id FROM notifications.provider_events"));
            var deliveries = await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM notifications.deliveries");
            var events = await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM notifications.provider_events");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(deliveries).IsEqualTo(1L);
                await Assert.That(events).IsEqualTo(1L);
                await Assert.That(readsId!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        });
    }

    [Test]
    public async Task Rerunning_the_grants_converges_on_the_same_matrix_even_after_a_grant_made_by_hand()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await ScratchDatabase.ExecuteAsync(owner, $"GRANT UPDATE ON notifications.deliveries TO {NotificationsDatabase.WorkerRole}");

            // Act
            await MigratedNotifications.MigrateAsync(owner);
            var held = await HeldAsync(owner, NotificationsDatabase.WorkerRole);

            // Assert
            await Assert.That(held).IsEquivalentTo(Matrix(WorkerGrants));
        });
    }

    private static string[] Matrix(Dictionary<string, string[]> grants) =>
        [.. grants.SelectMany(table => table.Value.Select(privilege => $"{table.Key}:{privilege}")).Order(StringComparer.Ordinal)];

    /// <summary>Every table-level (table, privilege) pair the role holds, from the full matrix of tables and privileges.</summary>
    private static async Task<string[]> HeldAsync(string owner, string role)
    {
        List<string> held = [];
        foreach (var table in Tables)
        {
            foreach (var privilege in Privileges)
            {
                var has = (bool)(await ScratchDatabase.ScalarAsync(
                    owner, $"SELECT has_table_privilege('{role}', 'notifications.{table}', '{privilege}')"))!;
                if (has)
                {
                    held.Add($"{table}:{privilege}");
                }
            }
        }

        return [.. held.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The columns of <paramref name="table"/> on which <paramref name="role"/> holds a column-level <paramref name="privilege"/> only.
    /// </summary>
    private static async Task<string[]> ColumnPrivilegesAsync(string owner, string role, string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT attname FROM pg_attribute
            WHERE attrelid = 'notifications.{table}'::regclass AND attnum > 0 AND NOT attisdropped
              AND has_column_privilege('{role}', attrelid, attnum, '{privilege}')
              AND NOT has_table_privilege('{role}', 'notifications.{table}', '{privilege}')
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        List<string> columns = [];
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return [.. columns];
    }
}
