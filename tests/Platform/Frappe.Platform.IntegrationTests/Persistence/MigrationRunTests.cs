using Frappe.Persistence;
using Frappe.Testing;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// The generic database setup on a real Postgres: one run at a time, roles before the migrations, and grants after them
/// that leave each module role data access to its own schema and nothing else.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class MigrationRunTests(PostgresServer server)
{
    private const string Password = "migration-run-test-password";

    /// <summary>Ends every role name this test creates, so its cleanup drops exactly those (roles belong to the cluster, not the database).</summary>
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    [Test]
    public async Task A_missing_password_fails_naming_the_key_before_connecting()
    {
        // Arrange
        var options = new MigrationOptions { OwnerConnectionString = "Host=127.0.0.1;Port=1;Timeout=1" };

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(
            async () => await MigrationRun.StartAsync(options, [new ModuleRoleGrant("Orders", "orders_role", "orders")], CancellationToken.None));

        // Assert
        await Assert.That(failure!.Failures).Contains(f => f.Contains("Roles:Orders:Password", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_second_run_waits_until_the_first_one_ends()
    {
        await WithDatabaseAsync(async (options, owner) =>
        {
            // Arrange
            var first = await MigrationRun.StartAsync(options, [], CancellationToken.None);

            // Act
            var lockFreeWhileHeld = await ScalarAsync(owner, $"SELECT pg_try_advisory_lock({MigrationRun.LockKey})") is true;
            var second = MigrationRun.StartAsync(options, [], CancellationToken.None);
            try
            {
                await first.DisposeAsync();
                await WaitUntil.TrueAsync(() => Task.FromResult(second.IsCompleted), "the second run never took the lock");

                // Assert
                await Assert.That(lockFreeWhileHeld).IsFalse();
            }
            finally
            {
                await (await second).DisposeAsync();
            }
        });
    }

    [Test]
    public async Task The_lock_is_free_again_once_the_run_is_disposed()
    {
        await WithDatabaseAsync(async (options, _) =>
        {
            // Arrange
            await (await MigrationRun.StartAsync(options, [], CancellationToken.None)).DisposeAsync();

            // Act
            await using var next = await MigrationRun.StartAsync(options, [], CancellationToken.None);

            // Assert
            await Assert.That(next.Connection.State).IsEqualTo(System.Data.ConnectionState.Open);
        });
    }

    [Test]
    public async Task A_lock_that_cannot_be_released_does_not_hide_the_failure_of_the_run()
    {
        await WithDatabaseAsync(async (options, owner) =>
        {
            // Arrange
            async Task RunThatFailsAfterLosingItsSessionAsync()
            {
                await using var run = await MigrationRun.StartAsync(options, [], CancellationToken.None);
                await ScalarAsync(owner, $"SELECT pg_terminate_backend({run.Connection.ProcessID})");
                throw new InvalidOperationException("The run failed on its own.");
            }

            // Act
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(RunThatFailsAfterLosingItsSessionAsync);
            await using var next = await MigrationRun.StartAsync(options, [], CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(failure!.Message).IsEqualTo("The run failed on its own.");
                await Assert.That(next.Connection.State).IsEqualTo(System.Data.ConnectionState.Open);
            }
        });
    }

    [Test]
    public async Task The_role_phase_creates_each_role_with_its_password_and_session_limits()
    {
        await WithDatabaseAsync(async (options, owner) =>
        {
            // Arrange
            var roles = NewRoles("orders", "billing");
            await using var run = await MigrationRun.StartAsync(WithPasswords(options, roles), roles, CancellationToken.None);

            // Act
            await run.EnsureRolesAsync(new DatabaseOptions { StatementTimeout = TimeSpan.FromSeconds(45) }, CancellationToken.None);

            // Assert
            foreach (var role in roles)
            {
                await Assert.That(await ShowAsync(owner, role.Role, "statement_timeout")).IsEqualTo("45s");
            }
        });
    }

    [Test]
    public async Task The_grant_phase_gives_a_role_data_access_to_its_own_schema_and_none_to_another()
    {
        await WithDatabaseAsync(async (options, owner) =>
        {
            // Arrange
            var roles = NewRoles("orders", "billing");
            await using var run = await MigrationRun.StartAsync(WithPasswords(options, roles), roles, CancellationToken.None);
            await run.EnsureRolesAsync(new DatabaseOptions(), CancellationToken.None);
            foreach (var schema in roles.Select(r => r.Schema))
            {
                await ExecuteAsync(owner, $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.item (id int PRIMARY KEY, name text); CREATE TABLE {schema}.{ModuleDatabase.MigrationsHistoryTable} (id int)");
            }

            // Act
            await run.GrantAsync(CancellationToken.None);
            var first = roles[0];
            var second = roles[1];
            await ExecuteAsync(owner, $"INSERT INTO {first.Schema}.item VALUES (1, 'x')", first.Role);
            var read = await ScalarAsync(owner, $"SELECT name FROM {first.Schema}.item", first.Role);
            var otherSchema = await Assert.ThrowsAsync<PostgresException>(async () => await ScalarAsync(owner, $"SELECT name FROM {second.Schema}.item", first.Role));
            var ddl = await Assert.ThrowsAsync<PostgresException>(async () => await ExecuteAsync(owner, $"CREATE TABLE {first.Schema}.other (id int)", first.Role));
            var history = await Assert.ThrowsAsync<PostgresException>(async () => await ScalarAsync(owner, $"SELECT * FROM {first.Schema}.{ModuleDatabase.MigrationsHistoryTable}", first.Role));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(read).IsEqualTo("x");
                await Assert.That(otherSchema!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(ddl!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await Assert.That(history!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        });
    }

    [Test]
    public async Task Running_the_phases_again_keeps_the_grants_and_covers_a_table_created_since()
    {
        await WithDatabaseAsync(async (options, owner) =>
        {
            // Arrange
            var roles = NewRoles("orders");
            var role = roles.Single();
            await using (var run = await MigrationRun.StartAsync(WithPasswords(options, roles), roles, CancellationToken.None))
            {
                await run.EnsureRolesAsync(new DatabaseOptions(), CancellationToken.None);
                await ExecuteAsync(owner, $"CREATE SCHEMA {role.Schema}; CREATE TABLE {role.Schema}.item (id int)");
                await run.GrantAsync(CancellationToken.None);
            }

            await ExecuteAsync(owner, $"CREATE TABLE {role.Schema}.later (id int)");

            // Act
            await using (var again = await MigrationRun.StartAsync(WithPasswords(options, roles), roles, CancellationToken.None))
            {
                await again.EnsureRolesAsync(new DatabaseOptions(), CancellationToken.None);
                await again.GrantAsync(CancellationToken.None);
            }

            await ExecuteAsync(owner, $"CREATE TABLE {role.Schema}.after_grant (id int)");

            // Assert
            await ExecuteAsync(owner, $"INSERT INTO {role.Schema}.item VALUES (1)", role.Role);
            await ExecuteAsync(owner, $"INSERT INTO {role.Schema}.later VALUES (1)", role.Role);
            await ExecuteAsync(owner, $"INSERT INTO {role.Schema}.after_grant VALUES (1)", role.Role);
        });
    }

    [Test]
    public async Task The_grant_phase_lets_a_role_use_identity_sequences_and_closes_security_definer_functions()
    {
        await WithDatabaseAsync(async (options, owner) =>
        {
            // Arrange
            var roles = NewRoles("orders");
            var role = roles.Single();
            await using var run = await MigrationRun.StartAsync(WithPasswords(options, roles), roles, CancellationToken.None);
            await run.EnsureRolesAsync(new DatabaseOptions(), CancellationToken.None);
            await ExecuteAsync(owner, $"""
                CREATE SCHEMA {role.Schema};
                CREATE TABLE {role.Schema}.item (id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name text);
                CREATE FUNCTION {role.Schema}.plain() RETURNS int LANGUAGE sql AS 'SELECT 1';
                CREATE FUNCTION {role.Schema}.closed() RETURNS int LANGUAGE sql SECURITY DEFINER AS 'SELECT 2';
                """);

            // Act
            await run.GrantAsync(CancellationToken.None);
            await ExecuteAsync(owner, $"INSERT INTO {role.Schema}.item (name) VALUES ('x')", role.Role);
            var plain = await ScalarAsync(owner, $"SELECT {role.Schema}.plain()", role.Role);
            var closed = await Assert.ThrowsAsync<PostgresException>(async () => await ScalarAsync(owner, $"SELECT {role.Schema}.closed()", role.Role));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(plain).IsEqualTo(1);
                await Assert.That(closed!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        });
    }

    [Test]
    [Arguments("Orders")]
    [Arguments("\"orders\"")]
    [Arguments("orders\n")]
    [Arguments("orders; DROP ROLE x")]
    public async Task A_role_or_schema_name_that_is_not_a_plain_lowercase_identifier_is_refused(string name)
    {
        // Arrange
        await using var connection = new NpgsqlConnection();

        // Act
        var role = await Assert.ThrowsAsync<ArgumentException>(async () => await ModuleRoles.GrantDataAccessAsync(connection, name, "orders", CancellationToken.None));
        var schema = await Assert.ThrowsAsync<ArgumentException>(async () => await ModuleRoles.GrantDataAccessAsync(connection, "orders_role", name, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(role).IsNotNull();
            await Assert.That(schema).IsNotNull();
        }
    }

    [Test]
    public async Task A_role_keeps_its_login_after_its_password_is_rotated_to_one_with_non_ascii_characters()
    {
        await WithDatabaseAsync(async (_, owner) =>
        {
            // Arrange
            var role = $"rot_{_suffix}";
            await using var connection = new NpgsqlConnection(owner);
            await connection.OpenAsync();
            await ModuleRoles.EnsureLoginRoleAsync(connection, role, Password, CancellationToken.None);

            // Act
            await ModuleRoles.EnsureLoginRoleAsync(connection, role, "contraseña-ñandú-ｆｕｌｌ", CancellationToken.None);

            // Assert
            await Assert.That(await ScalarAsync(owner, "SELECT 1", role, "contraseña-ñandú-ｆｕｌｌ")).IsEqualTo(1);
            await Assert.ThrowsAsync<PostgresException>(async () => await ScalarAsync(owner, "SELECT 1", role, Password));
        });
    }

    private static MigrationOptions WithPasswords(MigrationOptions options, IEnumerable<ModuleRoleGrant> roles)
    {
        foreach (var role in roles)
        {
            options.Roles[role.Name] = new ModuleRoleOptions { Password = Password };
        }

        return options;
    }

    private ModuleRoleGrant[] NewRoles(params string[] names)
    {
        return [.. names.Select(n => new ModuleRoleGrant(n, $"{n}_{_suffix}", $"{n}_data"))];
    }

    /// <summary>A database of its own (the lock and default privileges are per database): options for it and the owner's connection string.</summary>
    private async Task WithDatabaseAsync(Func<MigrationOptions, string, Task> test)
    {
        var name = "migration_run_" + Guid.NewGuid().ToString("N")[..12];
        var admin = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Pooling = false };
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE {name}");
        try
        {
            var owner = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name }.ConnectionString;
            var options = new MigrationOptions { OwnerConnectionString = owner };
            await test(options, owner);
        }
        finally
        {
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
            await ExecuteAsync(admin.ConnectionString, $"DO $$ DECLARE r name; BEGIN FOR r IN SELECT rolname FROM pg_roles WHERE rolname LIKE '%\\_{_suffix}' LOOP EXECUTE format('DROP ROLE %I', r); END LOOP; END $$");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql, string? role = null, string? password = null) =>
        await ScalarAsync(connectionString, sql, role, password);

    private static async Task<object?> ScalarAsync(string connectionString, string sql, string? role = null, string? password = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        if (role is not null)
        {
            builder.Username = role;
            builder.Password = password ?? Password;
        }

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<string> ShowAsync(string owner, string role, string setting) =>
        (string)(await ScalarAsync(owner, $"SHOW {setting}", role))!;
}
