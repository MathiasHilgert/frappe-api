using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Migrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Frappe.Geo.IntegrationTests.Hosts;

/// <summary>The Migrator against an empty database of the shared Postgres server: what a first deployment sees.</summary>
/// <remarks>
/// Each test gets its own empty database, but the Migrator's roles (<c>frappe_geo</c>, <c>frappe_scheduler</c>) belong
/// to the whole server and one test rotates their password, so these tests run one at a time among themselves.
/// </remarks>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[ParallelLimiter<MigratorTests.OneAtATime>]
public sealed class MigratorTests(PostgresServer server)
{
    public sealed class OneAtATime : TUnit.Core.Interfaces.IParallelLimit
    {
        public int Limit => 1;
    }

    // Quotes, backslashes, dollar quoting, format specifiers and non-ASCII: the role must still log in with it.
    private const string GeoPassword = "a'b\"c\\d$$e%I%L ñ€😀 ;--";
    private const string SchedulerPassword = "scheduler-test-password";

    private readonly List<string> _databases = [];

    /// <summary>Drops the databases this test created; they are not template clones, so nothing else drops them.</summary>
    [After(Test)]
    public async Task DropDatabasesAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        foreach (var name in _databases)
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private async Task<string> EmptyDatabaseAsync()
    {
        var name = "migrator_" + Guid.NewGuid().ToString("N")[..12];
        _databases.Add(name);
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await create.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = name }.ConnectionString;
    }

    private static IConfiguration Configuration(string owner, string? without = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Owner"] = owner,
            ["Roles:Geo:Password"] = GeoPassword,
            ["Roles:Scheduler:Password"] = SchedulerPassword,
        };
        if (without is not null)
        {
            values.Remove(without);
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static string As(string owner, string role, string password) =>
        new NpgsqlConnectionStringBuilder(owner) { Username = role, Password = password }.ConnectionString;

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static Task RunAsync(string owner) => DatabaseMigration.RunAsync(Configuration(owner), NullLogger.Instance, CancellationToken.None);

    [Test]
    public async Task An_empty_database_gets_the_Geo_schema_and_running_it_again_changes_nothing()
    {
        var owner = await EmptyDatabaseAsync();

        await RunAsync(owner);
        await RunAsync(owner);

        await Assert.That(await ScalarAsync(owner, "SELECT count(*) FROM geo.outbox_messages")).IsEqualTo(0L);
        await Assert.That(await ScalarAsync(owner, "SELECT count(*) FROM geo.inbox_messages")).IsEqualTo(0L);
        await Assert.That((long)(await ScalarAsync(owner, "SELECT count(*) FROM geo.__ef_migrations_history"))!).IsGreaterThan(0L);
        await Assert.That((long)(await ScalarAsync(owner, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'quartz'"))!)
            .IsGreaterThan(5L);
    }

    [Test]
    public async Task The_Geo_role_reads_and_writes_Geo_data_and_nothing_else()
    {
        var owner = await EmptyDatabaseAsync();
        await RunAsync(owner);
        var geo = As(owner, "frappe_geo", GeoPassword);

        await Assert.That(await ScalarAsync(geo, "SELECT count(*) FROM geo.outbox_messages")).IsEqualTo(0L);
        await Assert.That(await ScalarAsync(geo, "DELETE FROM geo.inbox_messages WHERE false")).IsNull();

        var history = await Assert.ThrowsAsync<PostgresException>(async () => await ScalarAsync(geo, "DELETE FROM geo.__ef_migrations_history"));
        var ddl = await Assert.ThrowsAsync<PostgresException>(async () => await ScalarAsync(geo, "CREATE TABLE geo.intruder (id int)"));
        var otherSchema = await Assert.ThrowsAsync<PostgresException>(async () => await ScalarAsync(geo, "SELECT count(*) FROM quartz.qrtz_locks"));
        await Assert.That(history!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(ddl!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(otherSchema!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task Only_a_SCRAM_verifier_reaches_the_server_and_a_rerun_rotates_the_password()
    {
        var owner = await EmptyDatabaseAsync();
        await RunAsync(owner);
        var stored = (string)(await ScalarAsync(owner, "SELECT rolpassword FROM pg_authid WHERE rolname = 'frappe_geo'"))!;

        await DatabaseMigration.RunAsync(
            new ConfigurationBuilder().AddConfiguration(Configuration(owner))
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Roles:Geo:Password"] = "rotated" }).Build(),
            NullLogger.Instance,
            CancellationToken.None);

        await Assert.That(stored).StartsWith("SCRAM-SHA-256$4096:");
        await Assert.That(await ScalarAsync(As(owner, "frappe_geo", "rotated"), "SELECT 1")).IsEqualTo(1);
        await Assert.ThrowsAsync<PostgresException>(async () => await ScalarAsync(As(owner, "frappe_geo", GeoPassword), "SELECT 1"));
        await RunAsync(owner);
    }

    [Test]
    public async Task Two_Migrators_started_together_both_succeed()
    {
        var owner = await EmptyDatabaseAsync();

        await Task.WhenAll(RunAsync(owner), RunAsync(owner));

        await Assert.That(await ScalarAsync(owner, "SELECT count(*) FROM geo.outbox_messages")).IsEqualTo(0L);
    }

    [Test]
    public async Task The_scheduler_role_reads_and_writes_Quartz_tables_and_cannot_see_Geo()
    {
        var owner = await EmptyDatabaseAsync();
        await RunAsync(owner);
        var scheduler = As(owner, "frappe_scheduler", SchedulerPassword);

        await Assert.That(await ScalarAsync(scheduler, "SELECT count(*) FROM quartz.qrtz_locks")).IsNotNull();

        var otherSchema = await Assert.ThrowsAsync<PostgresException>(async () => await ScalarAsync(scheduler, "SELECT count(*) FROM geo.outbox_messages"));
        await Assert.That(otherSchema!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    [Arguments("ConnectionStrings:Owner")]
    [Arguments("Roles:Geo:Password")]
    [Arguments("Roles:Scheduler:Password")]
    public async Task Missing_configuration_fails_before_touching_the_database_naming_the_key(string key)
    {
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
            await DatabaseMigration.RunAsync(Configuration("Host=unreachable.invalid", without: key), NullLogger.Instance, CancellationToken.None));

        await Assert.That(failure!.Message).Contains($"Configuration '{key}'");
    }

    [Test]
    public async Task The_host_exits_with_0_after_migrating_and_has_stopped_by_then()
    {
        using var host = MigratorHost(Configuration(await EmptyDatabaseAsync()));

        var exitCode = await host.RunMigrationAsync();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task The_host_exits_with_1_when_the_migration_fails_and_has_stopped_by_then()
    {
        using var host = MigratorHost(Configuration("Host=unreachable.invalid", without: "ConnectionStrings:Owner"));

        var exitCode = await host.RunMigrationAsync();

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested).IsTrue();
    }

    /// <summary>The Migrator's host as <c>Program</c> builds it, reading only the given configuration.</summary>
    private static IHost MigratorHost(IConfiguration configuration)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddConfiguration(configuration);
        builder.AddFrappeMigrator();
        return builder.Build();
    }
}
