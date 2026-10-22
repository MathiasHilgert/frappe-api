using StackExchange.Redis;

namespace Frappe.Platform.Caching;

/// <summary>
/// One shared, lazily and asynchronously opened multiplexer. Nothing blocks a thread on connect, and a Valkey
/// outage never blocks startup.
/// </summary>
public sealed class ValkeyConnection(ConfigurationOptions options) : IAsyncDisposable
{
    private readonly Lazy<Task<IConnectionMultiplexer>> _connection =
        new(async () => await ConnectionMultiplexer.ConnectAsync(options));

    public Task<IConnectionMultiplexer> GetAsync() => _connection.Value;

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
            await (await _connection.Value).DisposeAsync();
    }
}
