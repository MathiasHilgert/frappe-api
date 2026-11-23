using Npgsql;

namespace Frappe.Persistence;

/// <summary>
/// The one lock that makes two Migrators started together (two replicas of a deployment) run one after the other, never
/// interleaved: a session-level advisory lock held on the owner's connection for the whole run.
/// </summary>
public sealed class MigrationLock : IAsyncDisposable
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
    /// would wait for each other forever. Between tries this session holds no transaction.
    /// </summary>
    public static async Task<MigrationLock> AcquireAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", Key);
        while (await command.ExecuteScalarAsync(cancellationToken) is not true)
        {
            await Task.Delay(RetryDelay, cancellationToken);
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
}
