using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Frappe.Persistence;

/// <summary>
/// The one lock that makes two Migrators started together (two replicas of a deployment) run one after the other, never
/// interleaved: a session-level advisory lock held on the owner's connection for the whole run.
/// </summary>
public sealed partial class MigrationLock : IAsyncDisposable
{
    /// <summary>Session-level advisory lock key, "FRAPPE1".</summary>
    private const long Key = 0x46524150504531;

    /// <summary>How long a Migrator waits before trying the lock again while another one holds it.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly NpgsqlConnection _connection;

    private MigrationLock(NpgsqlConnection connection) => _connection = connection;

    /// <summary>
    /// Takes the lock on <paramref name="connection"/> by trying it until it is free, never by blocking on it. A session blocked
    /// inside <c>pg_advisory_lock</c> keeps a transaction open while it waits, and a migration that builds an index
    /// <c>CONCURRENTLY</c> waits for every open transaction to end: the Migrator holding the lock and the one waiting for it
    /// would wait for each other forever. Between tries this session holds no transaction. How long it took is recorded
    /// on <see cref="PersistenceMetrics.MigrationLockWaitMetric"/>, and a wait is logged when <paramref name="logger"/> is given.
    /// </summary>
    /// <param name="connection">The owner's connection, which keeps the lock until it is released.</param>
    /// <param name="cancellationToken">Gives up waiting.</param>
    /// <param name="logger">Where a wait is logged; none logs nothing.</param>
    /// <param name="metrics">Where the wait is measured; none uses the platform's own instruments.</param>
    public static async Task<MigrationLock> AcquireAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken,
        ILogger? logger = null,
        PersistenceMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var startedAt = Stopwatch.GetTimestamp();
        var waiting = false;
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", Key);
        while (await command.ExecuteScalarAsync(cancellationToken) is not true)
        {
            if (!waiting && logger is not null)
            {
                LogWaiting(logger);
            }

            waiting = true;
            await Task.Delay(RetryDelay, cancellationToken);
        }

        var waited = Stopwatch.GetElapsedTime(startedAt);
        (metrics ?? PersistenceMetrics.Standalone).MigrationLockWaited(waited);
        if (waiting && logger is not null)
        {
            LogAcquired(logger, waited.TotalSeconds);
        }

        return new MigrationLock(connection);
    }

    /// <summary>
    /// Releases the lock explicitly: a pooled connection keeps its session, so closing it would not release the lock right away.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", _connection);
        unlock.Parameters.AddWithValue("key", Key);
        await unlock.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Another Migrator holds the migration lock; waiting for it")]
    private static partial void LogWaiting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Took the migration lock after waiting {Seconds:F1} s")]
    private static partial void LogAcquired(ILogger logger, double seconds);
}
