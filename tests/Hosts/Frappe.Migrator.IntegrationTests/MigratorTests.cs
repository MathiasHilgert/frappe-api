using Frappe.Persistence;
using Frappe.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Frappe.Migrator.IntegrationTests;

/// <summary>
/// The Migrator as deployed: with zero modules it still provisions the scheduler's schema and role, it takes the migration lock,
/// and it sets up the modules it is given with the passwords it is configured with. The scheduler's role is cluster-wide, so
/// the tests run one at a time.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class MigratorTests(PostgresServer server)
{
    private const string SchedulerRole = "frappe_scheduler";
    private const string SchedulerPassword = "scheduler-test-password";

    private const string QuartzSchemas = "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'quartz'";

    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(1);

    [Test]
    public async Task With_zero_modules_it_provisions_the_quartz_schema_and_the_scheduler_role()
    {
        await ScratchDatabase.RunAsync(server, [SchedulerRole], async owner =>
        {
            // Act
            var exitCode = await MigratorHost.RunAsync(MigratorHost.Settings(owner, SchedulerPassword));
            var schema = await ScratchDatabase.ScalarAsync(owner, QuartzSchemas);
            var tables = await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'quartz'");
            var login = (bool)(await ScratchDatabase.ScalarAsync(owner, $"SELECT rolcanlogin FROM pg_roles WHERE rolname = '{SchedulerRole}'"))!;
            var scheduler = ScratchDatabase.LoginOf(owner, SchedulerRole, SchedulerPassword);
            var jobs = await ScratchDatabase.ScalarAsync(scheduler, "SELECT count(*) FROM quartz.qrtz_job_details");
            var ddl = await Assert.ThrowsAsync<PostgresException>(
                () => ScratchDatabase.ScalarAsync(scheduler, "CREATE TABLE quartz.other (id integer)"));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(Modules.All).IsEmpty();
                await Assert.That(exitCode).IsEqualTo(0);
                await Assert.That(schema).IsEqualTo(1L);
                await Assert.That(tables).IsNotEqualTo(0L);
                await Assert.That(login).IsTrue();
                await Assert.That(jobs).IsEqualTo(0L);
                await Assert.That(ddl!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        });
    }

    [Test]
    public async Task Running_it_again_changes_nothing_and_succeeds()
    {
        await ScratchDatabase.RunAsync(server, [SchedulerRole], async owner =>
        {
            // Arrange
            var settings = MigratorHost.Settings(owner, SchedulerPassword);
            _ = await MigratorHost.RunAsync(settings);

            // Act
            var exitCode = await MigratorHost.RunAsync(settings);
            var scheduler = ScratchDatabase.LoginOf(owner, SchedulerRole, SchedulerPassword);
            var jobs = await ScratchDatabase.ScalarAsync(scheduler, "SELECT count(*) FROM quartz.qrtz_job_details");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(exitCode).IsEqualTo(0);
                await Assert.That(jobs).IsEqualTo(0L);
            }
        });
    }

    [Test]
    public async Task It_waits_for_the_migration_lock_and_runs_once_it_is_released()
    {
        await ScratchDatabase.RunAsync(server, [SchedulerRole], async owner =>
        {
            // Arrange
            await using var holder = new NpgsqlConnection(owner);
            await holder.OpenAsync();
            var held = await MigrationLock.AcquireAsync(holder, CancellationToken.None);
            var run = MigratorHost.RunAsync(MigratorHost.Settings(owner, SchedulerPassword));

            // Act
            var waiting = await Assert.ThrowsAsync<TimeoutException>(() => run.WaitAsync(GiveUp));
            var schemaWhileHeld = await ScratchDatabase.ScalarAsync(owner, QuartzSchemas);
            await held.DisposeAsync();
            await WaitUntil.TrueAsync(() => run.IsCompleted, "the Migrator never took the released lock", TimeSpan.FromSeconds(30));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(waiting).IsNotNull();
                await Assert.That(schemaWhileHeld).IsEqualTo(0L);
                await Assert.That(await run).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task A_missing_role_password_fails_naming_the_key_before_the_database_is_touched()
    {
        await ScratchDatabase.RunAsync(server, [SchedulerRole], async owner =>
        {
            // Arrange
            var settings = new Dictionary<string, string?> { ["ConnectionStrings:Owner"] = owner };
            using var host = MigratorHost.Build(settings);
            var configuration = host.Services.GetRequiredService<IConfiguration>();

            // Act
            var failure = await Assert.ThrowsAsync<OptionsValidationException>(
                () => DatabaseMigration.RunAsync(configuration, [], NullLogger.Instance, CancellationToken.None));
            var exitCode = await MigratorHost.RunAsync(settings);
            var schema = await ScratchDatabase.ScalarAsync(owner, QuartzSchemas);
            var role = await ScratchDatabase.ScalarAsync(owner, $"SELECT count(*) FROM pg_roles WHERE rolname = '{SchedulerRole}'");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(failure!.Message).Contains("Roles:Scheduler:Password");
                await Assert.That(exitCode).IsEqualTo(1);
                await Assert.That(schema).IsEqualTo(0L);
                await Assert.That(role).IsEqualTo(0L);
            }
        });
    }

    [Test]
    public async Task A_missing_owner_connection_string_fails_naming_the_key()
    {
        // Arrange
        var settings = new Dictionary<string, string?> { ["Roles:Scheduler:Password"] = SchedulerPassword };

        // Act
        var exitCode = await MigratorHost.RunAsync(settings);
        using var host = MigratorHost.Build(settings);
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(
            () => DatabaseMigration.RunAsync(configuration, [], NullLogger.Instance, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(1);
            await Assert.That(failure!.Message).Contains("ConnectionStrings:Owner");
        }
    }

    [Test]
    public async Task A_listed_module_is_set_up_with_the_password_of_its_role_after_the_platform()
    {
        // Arrange
        var role = "mig_role_" + Guid.NewGuid().ToString("N")[..10];
        var schema = "mig_schema_" + Guid.NewGuid().ToString("N")[..10];
        var module = new TestModule(schema, role);

        await ScratchDatabase.RunAsync(server, [SchedulerRole, role], async owner =>
        {
            var settings = MigratorHost.Settings(owner, SchedulerPassword);
            settings["Roles:Orders:Password"] = "orders-test-password";

            // Act
            var exitCode = await MigratorHost.RunAsync(settings, [module]);
            var login = ScratchDatabase.LoginOf(owner, role, "orders-test-password");
            await ScratchDatabase.ScalarAsync(login, $"INSERT INTO {schema}.items VALUES (1)");
            var count = await ScratchDatabase.ScalarAsync(login, $"SELECT count(*) FROM {schema}.items");
            var quartz = await ScratchDatabase.ScalarAsync(owner, QuartzSchemas);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(exitCode).IsEqualTo(0);
                await Assert.That(count).IsEqualTo(1L);
                await Assert.That(quartz).IsEqualTo(1L);
            }
        });
    }

    [Test]
    public async Task A_listed_module_without_its_role_password_fails_naming_the_key()
    {
        // Arrange
        var module = new TestModule("mig_schema_unused", "mig_role_unused");
        var settings = MigratorHost.Settings("Host=unreachable.invalid;Database=frappe;Username=frappe_owner", SchedulerPassword);
        using var host = MigratorHost.Build(settings, [module]);
        var configuration = host.Services.GetRequiredService<IConfiguration>();

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(
            () => DatabaseMigration.RunAsync(configuration, [module], NullLogger.Instance, CancellationToken.None));

        // Assert
        await Assert.That(failure!.Message).Contains("Roles:Orders:Password");
    }

    /// <summary>A module with one schema, one table and one login role whose password is configured under <c>Roles:Orders</c>.</summary>
    private sealed class TestModule(string schema, string role) : IDatabaseModule
    {
        public string Name => "orders";

        public string Schema => schema;

        public IReadOnlyList<DatabaseRole> Roles { get; } = [new(role, "Orders", RoleGrant.Schema)];

        public Task MigrateAsync(string ownerConnectionString, CancellationToken cancellationToken) =>
            ScratchDatabase.ScalarAsync(ownerConnectionString, $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.items (id integer PRIMARY KEY)");
    }
}
