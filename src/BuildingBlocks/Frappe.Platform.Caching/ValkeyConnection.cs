using StackExchange.Redis;

namespace Frappe.Platform.Caching;

/// <summary>
/// One shared, lazily and asynchronously opened multiplexer. Nothing blocks a thread on connect, and a Valkey
/// outage never blocks startup. Concurrent callers share one connect attempt; a failed attempt is dropped, so the
/// next call tries again instead of failing forever.
/// </summary>
public sealed class ValkeyConnection : IAsyncDisposable
{
    private readonly Func<Task<IConnectionMultiplexer>> _connect;
    private readonly Lock _gate = new();
    private Task<IConnectionMultiplexer>? _connection;

    public ValkeyConnection(ConfigurationOptions options)
        : this(async () => await ConnectionMultiplexer.ConnectAsync(options)) { }

    internal ValkeyConnection(Func<Task<IConnectionMultiplexer>> connect) => _connect = connect;

    public Task<IConnectionMultiplexer> GetAsync()
    {
        lock (_gate)
        {
            if (_connection is null || _connection.IsFaulted || _connection.IsCanceled)
                _connection = _connect();

            return _connection;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<IConnectionMultiplexer>? connection;
        lock (_gate)
            connection = _connection;

        if (connection is { IsCompletedSuccessfully: true })
            await (await connection).DisposeAsync();
    }
}
