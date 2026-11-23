using Frappe.Persistence;
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

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
