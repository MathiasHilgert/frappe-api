using StackExchange.Redis;

namespace Frappe.Caching;

/// <summary>
/// Ephemeral markers in Valkey, shared by every replica, under the cache's instance name and its own <c>marker:</c> sub-prefix,
/// so no key collides with a cache entry, a value or a rate-limit window: a marker is opened only if
/// absent and closed only if present, each in one atomic command, and every marker expires on its own. Not a cache: a
/// marker lost to an outage is not rebuilt from anywhere. Uses its own connection, opened once and opened again on the
/// next call if that attempt failed. It never waits for Valkey longer than its connection's timeouts; callers bound it
/// further with their token.
/// </summary>
public sealed class ValkeyMarkers : IAsyncDisposable
{
    private readonly ValkeyConnection _connection;
    private readonly string _prefix;

    internal ValkeyMarkers(ConfigurationOptions options, string instanceName)
        : this(() => ConnectionMultiplexer.ConnectAsync(options), instanceName)
    {
    }

    /// <summary>With the connection opened by <paramref name="connect"/>, called again whenever the last attempt failed.</summary>
    internal ValkeyMarkers(Func<Task<ConnectionMultiplexer>> connect, string instanceName)
    {
        _connection = new ValkeyConnection(connect);
        _prefix = $"{instanceName}:marker:";
    }

    /// <summary>
    /// Opens the marker <paramref name="key"/> for <paramref name="lifetime"/> (one atomic set-if-absent with an
    /// expiry); <see langword="true"/> only when it was not open.
    /// </summary>
    public async Task<bool> TryOpenAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        return await connection.GetDatabase()
            .StringSetAsync(_prefix + key, 1, lifetime, When.NotExists)
            .WaitAsync(cancellationToken);
    }

    /// <summary>Closes the marker <paramref name="key"/> (one atomic delete); <see langword="true"/> only when it was open.</summary>
    public async Task<bool> TryCloseAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        return await connection.GetDatabase()
            .KeyDeleteAsync(_prefix + key)
            .WaitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
