using Frappe.Persistence;
using Frappe.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Frappe.Migrator.IntegrationTests;

/// <summary>The Migrator process against an empty database: what a first deployment of a host with no modules sees.</summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class MigratorRunTests(PostgresServer server)
{
    [Test]
    public async Task A_run_with_no_modules_takes_the_lock_completes_and_exits_0()
    {
        await WithDatabaseAsync(async owner =>
        {
            // Arrange
            using var host = BuildHost(owner);

            // Act
            var exitCode = await host.RunMigrationAsync();

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(exitCode).IsEqualTo(0);
                await Assert.That(await ScalarAsync(owner, $"SELECT pg_try_advisory_lock({MigrationRun.LockKey})") is true).IsTrue().Because("the run releases the lock when it ends");
                await Assert.That(await ScalarAsync(owner, "SELECT count(*) FROM pg_default_acl")).IsNotEqualTo(0L).Because("the revoke from PUBLIC runs even with no modules");
            }
        });
    }

    [Test]
    public async Task A_second_run_waits_while_another_session_holds_the_lock_then_completes()
    {
        await WithDatabaseAsync(async owner =>
        {
            // Arrange
            await using var holder = new NpgsqlConnection(owner);
            await holder.OpenAsync();
            await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({MigrationRun.LockKey})", holder))
            {
                await take.ExecuteNonQueryAsync();
            }

            using var host = BuildHost(owner);

            // Act
            var run = host.RunMigrationAsync();
            await WaitUntil.TrueAsync(
                async () => await ScalarAsync(owner, "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%pg_try_advisory_lock%' AND pid <> pg_backend_pid()") is > 0L,
                "the run never tried the lock");
            var finishedWhileHeld = run.IsCompleted;
            await using (var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({MigrationRun.LockKey})", holder))
            {
                await release.ExecuteNonQueryAsync();
            }

            await WaitUntil.TrueAsync(() => Task.FromResult(run.IsCompleted), "the run never finished once the lock was free");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(finishedWhileHeld).IsFalse();
                await Assert.That(await run).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task A_missing_owner_connection_string_fails_naming_the_key_before_connecting()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
            await DatabaseMigration.RunAsync(configuration, [], NullLogger.Instance, CancellationToken.None));

        // Assert
        await Assert.That(failure!.Message).Contains("ConnectionStrings:Owner");
    }

    [Test]
    public async Task An_invalid_session_timeout_fails_naming_the_key_before_connecting()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:Owner", "Host=127.0.0.1;Port=1;Timeout=1"),
                new KeyValuePair<string, string?>("Database:StatementTimeout", "-1"),
            ])
            .Build();

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
            await DatabaseMigration.RunAsync(configuration, [], NullLogger.Instance, CancellationToken.None));

        // Assert
        await Assert.That(failure!.Message).Contains("Database:StatementTimeout");
    }

    [Test]
    public async Task A_missing_owner_connection_string_makes_the_process_exit_1()
    {
        // Arrange
        using var host = BuildHost(null);

        // Act
        var exitCode = await host.RunMigrationAsync();

        // Assert
        await Assert.That(exitCode).IsEqualTo(1);
    }

    [Test]
    public async Task A_host_that_fails_to_start_makes_the_process_exit_1()
    {
        // Arrange
        var service = new LifecycleProbe(failToStart: true);
        using var host = BuildHost(null, configure: services => services.AddSingleton<IHostedService>(service));

        // Act
        var exitCode = await host.RunMigrationAsync();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(1);
            await Assert.That(service.Stops).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_host_that_fails_to_stop_is_stopped_once_and_keeps_the_exit_code_of_the_migration()
    {
        await WithDatabaseAsync(async owner =>
        {
            // Arrange
            var service = new LifecycleProbe(failToStop: true);
            using var host = BuildHost(owner, configure: services => services.AddSingleton<IHostedService>(service));

            // Act
            var exitCode = await host.RunMigrationAsync();

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(exitCode).IsEqualTo(0);
                await Assert.That(service.Stops).IsEqualTo(1);
            }
        });
    }

    private static IHost BuildHost(string? owner, string? statementTimeout = null, Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        if (owner is not null)
        {
            builder.Configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("ConnectionStrings:Owner", owner)]);
        }

        if (statementTimeout is not null)
        {
            builder.Configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("Database:StatementTimeout", statementTimeout)]);
        }

        builder.AddFrappeMigrator();
        configure?.Invoke(builder.Services);
        return builder.Build();
    }

    private async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var name = "migrator_" + Guid.NewGuid().ToString("N")[..12];
        var admin = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Pooling = false }.ConnectionString;
        await ExecuteAsync(admin, $"CREATE DATABASE {name}");
        try
        {
            await test(new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString);
        }
        finally
        {
            await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>A part of the host that can fail to start or to stop, and counts how many times it was stopped.</summary>
    private sealed class LifecycleProbe(bool failToStart = false, bool failToStop = false) : IHostedService
    {
        public int Stops { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) =>
            failToStart ? Task.FromException(new InvalidOperationException("The probe failed to start.")) : Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            return failToStop ? Task.FromException(new InvalidOperationException("The probe failed to stop.")) : Task.CompletedTask;
        }
    }
}
