using StackExchange.Redis;

namespace Frappe.Caching;

/// <summary>
/// One Valkey connection shared by the callers of a Valkey adapter: opened on first use, opened again on the next use
/// if that attempt failed, and closed with the host. Once disposed it opens no new one, since nobody would close it.
/// </summary>
internal sealed class ValkeyConnection(Func<Task<ConnectionMultiplexer>> connect) : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private Task<ConnectionMultiplexer>? _connection;
    private bool _disposed;

    /// <summary>The connection, opened once; an attempt that failed is replaced by a new one, so a replica recovers.</summary>
    public Task<ConnectionMultiplexer> GetAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is null or { IsFaulted: true } or { IsCanceled: true })
            {
                _connection = connect();
            }

            return _connection;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<ConnectionMultiplexer>? connection;
        lock (_gate)
        {
            _disposed = true;
            connection = _connection;
        }

        if (connection is null or { IsFaulted: true } or { IsCanceled: true })
        {
            return;
        }

        // A connection still opening is awaited, or it would outlive the host; a failed one has nothing to close.
        ConnectionMultiplexer opened;
        try
        {
            opened = await connection;
        }
#pragma warning disable CA1031 // An attempt that fails while the host stops has nothing to close and must not fail the shutdown.
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        await opened.DisposeAsync();
    }
}
