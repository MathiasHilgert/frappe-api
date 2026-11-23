using System.Diagnostics.Metrics;
using Frappe.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>Two Migrators started together run one after the other: the second waits for the first, and a released lock is free at once.</summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class MigrationLockTests(PostgresServer server)
{
    private static readonly TimeSpan GiveUp = TimeSpan.FromSeconds(1);

    [Test]
    public async Task A_second_migrator_cannot_take_the_lock_while_the_first_holds_it()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            await using var first = await OpenAsync(owner);
            await using var held = await MigrationLock.AcquireAsync(first, CancellationToken.None);
            await using var second = await OpenAsync(owner);
            using var giveUp = new CancellationTokenSource(GiveUp);

            // Act
            var waiting = await Assert.ThrowsAsync<OperationCanceledException>(() => MigrationLock.AcquireAsync(second, giveUp.Token));

            // Assert
            await Assert.That(waiting).IsNotNull();
        });
    }

    [Test]
    public async Task The_lock_is_free_again_once_the_first_migrator_releases_it()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            await using var first = await OpenAsync(owner);
            await using var second = await OpenAsync(owner);
            var held = await MigrationLock.AcquireAsync(first, CancellationToken.None);

            // Act
            await held.DisposeAsync();
            using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var taken = await MigrationLock.AcquireAsync(second, giveUp.Token);

            // Assert
            await Assert.That(taken).IsNotNull();
        });
    }

    [Test]
    public async Task A_waiting_migrator_takes_the_lock_as_soon_as_it_is_released()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            await using var first = await OpenAsync(owner);
            await using var second = await OpenAsync(owner);
            var held = await MigrationLock.AcquireAsync(first, CancellationToken.None);
            using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var waiting = MigrationLock.AcquireAsync(second, giveUp.Token);

            // Act
            await held.DisposeAsync();
            await using var taken = await waiting;

            // Assert
            await Assert.That(taken).IsNotNull();
        });
    }

    [Test]
    public async Task A_migrator_that_waited_logs_it_and_measures_the_wait_while_one_that_did_not_only_measures()
    {
        await ScratchDatabase.RunAsync(server, [], async owner =>
        {
            // Arrange
            using var meters = new ServiceCollection().AddMetrics().BuildServiceProvider();
            var factory = meters.GetRequiredService<IMeterFactory>();
            using var capture = new PersistenceMeterCapture(factory);
            var metrics = new PersistenceMetrics(factory);
            var logs = new List<string>();
            await using var first = await OpenAsync(owner);
            await using var second = await OpenAsync(owner);
            var held = await MigrationLock.AcquireAsync(first, CancellationToken.None, new ListLogger(logs), metrics);
            using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var waiting = MigrationLock.AcquireAsync(second, giveUp.Token, new ListLogger(logs), metrics);
            await Task.Delay(TimeSpan.FromMilliseconds(600), CancellationToken.None);

            // Act
            await held.DisposeAsync();
            await using var taken = await waiting;

            // Assert
            var waits = capture.Of(PersistenceMetrics.MigrationLockWaitMetric).Select(m => m.Value).Order().ToArray();
            using (Assert.Multiple())
            {
                await Assert.That(waits.Length).IsEqualTo(2);
                await Assert.That(waits[0]).IsLessThan(0.5);
                await Assert.That(waits[1]).IsGreaterThanOrEqualTo(0.5);
                await Assert.That(logs.Count(l => l.Contains("Another Migrator", StringComparison.Ordinal))).IsEqualTo(1);
                await Assert.That(logs.Count(l => l.Contains("after waiting", StringComparison.Ordinal))).IsEqualTo(1);
            }
        });
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>A logger that keeps the text of what it is told.</summary>
    private sealed class ListLogger(List<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (entries)
            {
                entries.Add(formatter(state, exception));
            }
        }
    }
}
