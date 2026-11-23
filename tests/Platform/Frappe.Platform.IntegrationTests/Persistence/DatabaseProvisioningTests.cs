using System.Collections.Concurrent;
using Frappe.Persistence;
using Frappe.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TUnit.Assertions.Enums;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// The Migrator's phases run the same for every database participant, module or platform capability: roles first, then the
/// migrations, then the database-wide revoke and the grants, all under one lock.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class DatabaseProvisioningTests(PostgresServer server)
{
    private const string Password = "provisioning-test-password";

    [Test]
    public async Task Roles_exist_before_the_first_migration_runs()
    {
        // Arrange
        var role = ScratchDatabase.Unique("prov_role");
        var schema = ScratchDatabase.Unique("orders");
        var existedDuringMigration = false;
        var participant = new TestParticipant("orders", schema, [SchemaRole(role)], async (owner, _) =>
        {
            existedDuringMigration = (bool)(await ScratchDatabase.ScalarAsync(
                owner,
                $"SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}')"))!;
            await ScratchDatabase.ExecuteAsync(owner, $"CREATE SCHEMA {schema}");
        });

        await ScratchDatabase.RunAsync(server, [role], async owner =>
        {
            // Act
            await RunAsync(owner, [participant]);

            // Assert
            await Assert.That(existedDuringMigration).IsTrue();
        });
    }

    [Test]
    public async Task A_schema_grant_gives_the_role_data_access_to_what_the_migration_created_and_no_ddl()
    {
        // Arrange
        var role = ScratchDatabase.Unique("prov_role");
        var schema = ScratchDatabase.Unique("orders");
        var participant = new TestParticipant("orders", schema, [SchemaRole(role)], (owner, _) => ScratchDatabase.ExecuteAsync(
            owner,
            $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.items (id integer PRIMARY KEY)"));

        await ScratchDatabase.RunAsync(server, [role], async owner =>
        {
            // Act
            await RunAsync(owner, [participant]);
            var login = LoginOf(owner, role);
            await ScratchDatabase.ExecuteAsync(login, $"INSERT INTO {schema}.items VALUES (1)");
            var count = await ScratchDatabase.ScalarAsync(login, $"SELECT count(*) FROM {schema}.items");
            var ddl = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(login, $"CREATE TABLE {schema}.other (id integer)"));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(count).IsEqualTo(1L);
                await Assert.That(ddl!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        });
    }

    [Test]
    public async Task A_custom_grant_gives_the_role_only_what_it_says()
    {
        // Arrange
        var role = ScratchDatabase.Unique("prov_role");
        var schema = ScratchDatabase.Unique("orders");
        var narrow = new DatabaseRole(role, "narrow", RoleGrant.Custom(async (owner, token) =>
        {
            await using var batch = new NpgsqlCommand($"GRANT USAGE ON SCHEMA {schema} TO {role}; GRANT SELECT ON {schema}.items TO {role}", owner);
            await batch.ExecuteNonQueryAsync(token);
        }));
        var participant = new TestParticipant("orders", schema, [narrow], (owner, _) => ScratchDatabase.ExecuteAsync(
            owner,
            $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.items (id integer PRIMARY KEY)"));

        await ScratchDatabase.RunAsync(server, [role], async owner =>
        {
            // Act
            await RunAsync(owner, [participant]);
            var login = LoginOf(owner, role);
            var count = await ScratchDatabase.ScalarAsync(login, $"SELECT count(*) FROM {schema}.items");
            var write = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ExecuteAsync(login, $"INSERT INTO {schema}.items VALUES (1)"));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(count).IsEqualTo(0L);
                await Assert.That(write!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        });
    }

    [Test]
    public async Task Every_role_runs_its_sessions_under_the_configured_timeouts()
    {
        // Arrange
        var role = ScratchDatabase.Unique("prov_role");
        var schema = ScratchDatabase.Unique("orders");
        var participant = new TestParticipant(
            "orders",
            schema,
            [SchemaRole(role)],
            (owner, _) => ScratchDatabase.ExecuteAsync(owner, $"CREATE SCHEMA {schema}"));

        await ScratchDatabase.RunAsync(server, [role], async owner =>
        {
            // Act
            await RunAsync(owner, [participant], new DatabaseOptions { StatementTimeout = TimeSpan.FromSeconds(45) });
            var timeout = await ScratchDatabase.ScalarAsync(LoginOf(owner, role), "SHOW statement_timeout");

            // Assert
            await Assert.That(timeout).IsEqualTo("45s");
        });
    }

    [Test]
    public async Task A_function_a_migration_creates_stays_callable_and_one_the_owner_creates_afterwards_does_not()
    {
        // Arrange
        var role = ScratchDatabase.Unique("prov_role");
        var schema = ScratchDatabase.Unique("orders");
        var participant = new TestParticipant("orders", schema, [SchemaRole(role)], (owner, _) => ScratchDatabase.ExecuteAsync(
            owner,
            $"CREATE SCHEMA {schema}; CREATE FUNCTION public.from_migration() RETURNS integer LANGUAGE sql AS 'SELECT 1'"));

        await ScratchDatabase.RunAsync(server, [role], async owner =>
        {
            // Act
            await RunAsync(owner, [participant]);
            await ScratchDatabase.ExecuteAsync(owner, "CREATE FUNCTION public.afterwards() RETURNS integer LANGUAGE sql AS 'SELECT 2'");
            var migrated = await PublicCanExecuteAsync(owner, "public.from_migration()");
            var later = await PublicCanExecuteAsync(owner, "public.afterwards()");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(migrated).IsTrue();
                await Assert.That(later).IsFalse();
            }
        });
    }

    [Test]
    public async Task Participants_migrate_in_the_order_given_and_every_run_is_repeatable()
    {
        // Arrange
        var order = new ConcurrentQueue<string>();
        var first = new TestParticipant("first", ScratchDatabase.Unique("first"), [], (_, _) => Record(order, "first"));
        var second = new TestParticipant("second", ScratchDatabase.Unique("second"), [], (_, _) => Record(order, "second"));

        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Act
            await RunAsync(owner, [first, second]);
            await RunAsync(owner, [first, second]);

            // Assert
            await Assert.That(order.ToArray()).IsEquivalentTo(["first", "second", "first", "second"], CollectionOrdering.Matching);
        });
    }

    [Test]
    public async Task Without_participants_only_the_database_wide_revoke_runs()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Act
            await RunAsync(owner, []);
            await ScratchDatabase.ExecuteAsync(owner, "CREATE FUNCTION public.answer() RETURNS integer LANGUAGE sql AS 'SELECT 42'");
            var publicCanExecute = await PublicCanExecuteAsync(owner, "public.answer()");

            // Assert
            await Assert.That(publicCanExecute).IsFalse();
        });
    }

    [Test]
    public async Task A_missing_password_stops_the_run_before_the_database_is_touched()
    {
        // Arrange
        var migrated = false;
        var participant = new TestParticipant("orders", "orders", [new DatabaseRole("frappe_orders", "Orders", RoleGrant.Schema)], (_, _) =>
        {
            migrated = true;
            return Task.CompletedTask;
        });

        // Act: the owner's connection string points nowhere, so touching the database would fail differently.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseProvisioning.RunAsync(
            "Host=127.0.0.1;Port=1;Timeout=1",
            [participant],
            new Dictionary<string, string>(),
            new DatabaseOptions(),
            NullLogger.Instance,
            CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure!.Message).Contains("frappe_orders");
            await Assert.That(failure.Message).Contains("Orders");
            await Assert.That(migrated).IsFalse();
        }
    }

    [Test]
    public async Task The_lock_is_held_for_the_whole_run_and_a_second_run_follows_the_first()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            var order = new ConcurrentQueue<string>();
            var inside = new TaskCompletionSource();
            var proceed = new TaskCompletionSource();
            var slow = new TestParticipant("slow", ScratchDatabase.Unique("slow"), [], async (_, _) =>
            {
                order.Enqueue("first enters");
                inside.SetResult();
                await proceed.Task;
                order.Enqueue("first leaves");
            });
            var fast = new TestParticipant("fast", ScratchDatabase.Unique("fast"), [], (_, _) => Record(order, "second runs"));
            var firstRun = RunAsync(owner, [slow]);
            await inside.Task;

            // Act
            await using var other = new NpgsqlConnection(owner);
            await other.OpenAsync();
            using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var blocked = await Assert.ThrowsAsync<OperationCanceledException>(() => MigrationLock.AcquireAsync(other, giveUp.Token));
            var secondRun = RunAsync(owner, [fast]);
            proceed.SetResult();
            await Task.WhenAll(firstRun, secondRun);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(blocked).IsNotNull();
                await Assert.That(order.ToArray()).IsEquivalentTo(["first enters", "first leaves", "second runs"], CollectionOrdering.Matching);
            }
        });
    }

    [Test]
    public async Task A_failed_migration_releases_the_lock_for_the_next_run()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            var broken = new TestParticipant("broken", "broken", [], (_, _) => throw new InvalidOperationException("migration failed"));
            var healthy = new TestParticipant("healthy", "healthy", [], (_, _) => Task.CompletedTask);

            // Act
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(owner, [broken]));
            var next = RunAsync(owner, [healthy]);
            await WaitUntil.TrueAsync(() => next.IsCompleted, "the next run never took the lock", TimeSpan.FromSeconds(10));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(failure!.Message).IsEqualTo("migration failed");
                await Assert.That(next.IsCompletedSuccessfully).IsTrue();
            }
        });
    }

    private static async Task<bool> PublicCanExecuteAsync(string owner, string function) =>
        (bool)(await ScratchDatabase.ScalarAsync(owner, $"SELECT has_function_privilege('public', '{function}', 'EXECUTE')"))!;

    private static DatabaseRole SchemaRole(string role) => new(role, role, RoleGrant.Schema);

    private static Task Record(ConcurrentQueue<string> order, string step)
    {
        order.Enqueue(step);
        return Task.CompletedTask;
    }

    private static Task RunAsync(string owner, IDatabaseParticipant[] participants, DatabaseOptions? limits = null) =>
        DatabaseProvisioning.RunAsync(
            owner,
            participants,
            participants.SelectMany(p => p.Roles).ToDictionary(role => role.ConfigName, _ => Password),
            limits ?? new DatabaseOptions(),
            NullLogger.Instance,
            CancellationToken.None);

    /// <summary>Unpooled: every check opens a fresh session, the only kind a changed role setting reaches.</summary>
    private static string LoginOf(string owner, string role) =>
        new NpgsqlConnectionStringBuilder(owner) { Username = role, Password = Password, Pooling = false }.ConnectionString;

    /// <summary>A participant whose migration is a callback.</summary>
    private sealed class TestParticipant(string name, string schema, IReadOnlyList<DatabaseRole> roles, Func<string, CancellationToken, Task> migrate)
        : IDatabaseModule
    {
        public string Name => name;

        public string Schema => schema;

        public IReadOnlyList<DatabaseRole> Roles => roles;

        public Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken) =>
            migrate(ownerConnectionString, cancellationToken);
    }
}
