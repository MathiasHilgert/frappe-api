using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Testing.Containers;
using Npgsql;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// What each Identity login role may do, as the full table-by-privilege matrix (MIG-11, MIG-12): the Api's role has data access on the schema
/// and no DDL; the Worker's role has the relay's grants, the outbox insert and, on the business tables the sweep and the purge touch, exactly
/// their columns and deletes (S16): never a hash, a token, an address, a name or a user agent.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class IdentityRolesTests(PostgresServer server)
{
    private static readonly string[] BusinessTables =
        ["principals", "email_identifiers", "password_credentials", "sessions", "one_time_codes", "email_change_undos", "owners"];

    private static readonly string[] Tables = [.. BusinessTables, "outbox_messages", "inbox_messages", "__ef_migrations_history"];

    private const string Sequence = "identity.outbox_messages_sequence_seq";

    private static readonly string[] Privileges = ["SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER"];

    private static readonly string[] OutboxRelayColumns = ["published_at", "attempts", "next_attempt_at", "leased_until", "last_error"];

    /// <summary>The columns the Worker may read, and the one it may update: what the sweep and the purge need and no more (S16).</summary>
    private static readonly Dictionary<string, string[]> WorkerReadColumns = new()
    {
        ["sessions"] = ["absolute_expires_at", "ended_at", "id", "idle_expires_at", "principal_id", "revoked_at"],
        ["one_time_codes"] = ["expires_at", "id"],
        ["owners"] = ["principal_id", "registered_at", "verified_at"],
        ["principals"] = ["id", "status"],
    };

    private static readonly string[] Dml = ["SELECT", "INSERT", "UPDATE", "DELETE"];

    private static Dictionary<string, string[]> ApiGrants() =>
        new[] { "outbox_messages", "inbox_messages" }.Concat(BusinessTables).ToDictionary(table => table, _ => Dml)
            .Append(new("__ef_migrations_history", [])).ToDictionary();

    private static readonly Dictionary<string, string[]> WorkerGrants = BusinessTables
        .ToDictionary(table => table, table => table is "sessions" or "one_time_codes" or "principals" ? new[] { "DELETE" } : [])
        .Concat(new Dictionary<string, string[]>
        {
            ["outbox_messages"] = ["SELECT", "INSERT", "DELETE"],
            ["inbox_messages"] = ["SELECT", "DELETE"],
            ["__ef_migrations_history"] = [],
        }).ToDictionary();

    [Test]
    public async Task The_Api_role_has_data_access_to_every_table_but_the_migration_history()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Act
            var held = await HeldAsync(owner, IdentityDatabase.ApiRole);

            // Assert
            await Assert.That(held).IsEquivalentTo(Matrix(ApiGrants()));
        });
    }

    [Test]
    public async Task The_Worker_role_holds_the_relay_grants_the_outbox_insert_and_only_the_deletes_of_the_purge()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Act
            var held = await HeldAsync(owner, IdentityDatabase.WorkerRole);

            // Assert
            await Assert.That(held).IsEquivalentTo(Matrix(WorkerGrants));
        });
    }

    [Test]
    public async Task The_Worker_reads_and_updates_only_the_columns_of_the_sweep_and_the_purge()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Act
            var outbox = await ColumnPrivilegesAsync(owner, IdentityDatabase.WorkerRole, "outbox_messages", "UPDATE");
            var readable = new Dictionary<string, string[]>();
            var updatable = new Dictionary<string, string[]>();
            var insertable = new List<string>();
            foreach (var table in BusinessTables)
            {
                var selectable = await ColumnPrivilegesAsync(owner, IdentityDatabase.WorkerRole, table, "SELECT");
                readable[table] = [.. selectable.Order(StringComparer.Ordinal)];
                updatable[table] = await ColumnPrivilegesAsync(owner, IdentityDatabase.WorkerRole, table, "UPDATE");
                insertable.AddRange(await ColumnPrivilegesAsync(owner, IdentityDatabase.WorkerRole, table, "INSERT"));
            }

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(outbox).IsEquivalentTo(OutboxRelayColumns);
                foreach (var table in BusinessTables)
                {
                    await Assert.That(readable[table]).IsEquivalentTo(WorkerReadColumns.GetValueOrDefault(table, []));
                    await Assert.That(updatable[table]).IsEquivalentTo(table == "sessions" ? new[] { "ended_at" } : []);
                }

                await Assert.That(insertable).IsEmpty();
            }
        });
    }

    [Test]
    public async Task The_Worker_writes_an_outbox_row_without_the_outbox_sequence_and_reads_no_business_row()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var worker = ScratchDatabase.LoginOf(owner, IdentityDatabase.WorkerRole, MigratedIdentity.WorkerPassword);

            // Act
            await ScratchDatabase.ExecuteAsync(worker, """
                INSERT INTO identity.outbox_messages (id, type, source, data_content_type, data, occurred_at, attempts)
                VALUES (gen_random_uuid(), 'frappe.identity.session_ended.v1', '/frappe/identity', 'application/json', '{}', now(), 0)
                """);
            var rows = await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM identity.outbox_messages");
            var sequence = (bool)(await ScratchDatabase.ScalarAsync(
                owner, $"SELECT has_sequence_privilege('{IdentityDatabase.WorkerRole}', '{Sequence}', 'USAGE')"))!;
            var readsSessions = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(worker, "SELECT token_hash FROM identity.sessions"));
            var readsEmails = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(worker, "SELECT address FROM identity.email_identifiers"));
            var secrets = new List<string?>();
            foreach (var column in new[]
            {
                "identity.password_credentials.hash", "identity.one_time_codes.code_hmac", "identity.one_time_codes.target",
                "identity.owners.given_name", "identity.owners.family_name", "identity.sessions.user_agent", "identity.sessions.ip_prefix",
            })
            {
                var parts = column.Split('.');
                var denied = await Assert.ThrowsAsync<PostgresException>(
                    () => ScratchDatabase.ExecuteAsync(worker, $"SELECT {parts[2]} FROM {parts[0]}.{parts[1]}"));
                secrets.Add(denied?.SqlState);
            }

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(rows).IsEqualTo(1L);
                await Assert.That(sequence).IsFalse();
                await Assert.That(readsSessions!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(readsEmails!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(secrets).IsEquivalentTo(Enumerable.Repeat<string?>(PostgresErrorCodes.InsufficientPrivilege, secrets.Count));
            }
        });
    }

    [Test]
    public async Task Neither_role_can_create_objects_in_the_schema_and_only_the_Api_may_use_the_outbox_sequence()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Act
            var api = ScratchDatabase.LoginOf(owner, IdentityDatabase.ApiRole, MigratedIdentity.ApiPassword);
            var worker = ScratchDatabase.LoginOf(owner, IdentityDatabase.WorkerRole, MigratedIdentity.WorkerPassword);
            var apiDdl = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(api, "CREATE TABLE identity.other (id integer)"));
            var workerDdl = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(worker, "CREATE TABLE identity.other (id integer)"));
            var apiSequence = (bool)(await ScratchDatabase.ScalarAsync(
                owner, $"SELECT has_sequence_privilege('{IdentityDatabase.ApiRole}', '{Sequence}', 'USAGE')"))!;

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(apiDdl!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(workerDdl!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(apiSequence).IsTrue();
            }
        });
    }

    [Test]
    [Arguments("api")]
    [Arguments("worker")]
    public async Task Neither_role_reaches_the_quartz_schema_or_creates_objects_in_public(string which)
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            var role = which == "api" ? IdentityDatabase.ApiRole : IdentityDatabase.WorkerRole;
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
    public async Task Rerunning_the_grants_converges_on_the_same_matrix_even_after_a_grant_made_by_hand()
    {
        await MigratedIdentity.RunAsync(server, async owner =>
        {
            // Arrange
            await ScratchDatabase.ExecuteAsync(owner, $"GRANT SELECT ON identity.sessions TO {IdentityDatabase.WorkerRole}");
            await ScratchDatabase.ExecuteAsync(owner, $"GRANT SELECT (token_hash) ON identity.sessions TO {IdentityDatabase.WorkerRole}");

            // Act
            await MigratedIdentity.MigrateAsync(owner);
            var held = await HeldAsync(owner, IdentityDatabase.WorkerRole);
            var sessionColumns = await ColumnPrivilegesAsync(owner, IdentityDatabase.WorkerRole, "sessions", "SELECT");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(held).IsEquivalentTo(Matrix(WorkerGrants));
                await Assert.That(sessionColumns).IsEquivalentTo(WorkerReadColumns["sessions"]);
            }
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
                    owner, $"SELECT has_table_privilege('{role}', 'identity.{table}', '{privilege}')"))!;
                if (has)
                {
                    held.Add($"{table}:{privilege}");
                }
            }
        }

        return [.. held.Order(StringComparer.Ordinal)];
    }

    /// <summary>Columns of <paramref name="table"/> where <paramref name="role"/> holds only a column-level <paramref name="privilege"/>.</summary>
    private static async Task<string[]> ColumnPrivilegesAsync(string owner, string role, string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            SELECT attname FROM pg_attribute
            WHERE attrelid = 'identity.{table}'::regclass AND attnum > 0 AND NOT attisdropped
              AND has_column_privilege('{role}', attrelid, attnum, '{privilege}')
              AND NOT has_table_privilege('{role}', 'identity.{table}', '{privilege}')
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
