using StackExchange.Redis;

namespace Frappe.Caching;

/// <summary>
/// Small values in Valkey, shared by every replica, under the cache's instance name and its own <c>value:</c> sub-prefix,
/// so no key collides with a cache entry, a marker or a rate-limit window: written only if absent, read,
/// overwritten and deleted, each in one atomic command, and every value expires on its own. Not a cache: a value lost to
/// an outage is not rebuilt from anywhere (the platform's <c>Idempotency-Key</c> answers live here). Uses its own
/// connection, opened once and opened again on the next call if that attempt failed. It never waits for Valkey longer
/// than its connection's timeouts; callers bound it further with their token.
/// </summary>
public sealed class ValkeyValues : IAsyncDisposable
{
    private readonly ValkeyConnection _connection;
    private readonly string _prefix;

    internal ValkeyValues(ConfigurationOptions options, string instanceName)
        : this(() => ConnectionMultiplexer.ConnectAsync(options), instanceName)
    {
    }

    /// <summary>With the connection opened by <paramref name="connect"/>, called again whenever the last attempt failed.</summary>
    internal ValkeyValues(Func<Task<ConnectionMultiplexer>> connect, string instanceName)
    {
        _connection = new ValkeyConnection(connect);
        _prefix = $"{instanceName}:value:";
    }

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> for <paramref name="lifetime"/> only if the key is absent;
    /// <see langword="true"/> when it was stored.
    /// </summary>
    public async Task<bool> TryAddAsync(string key, byte[] value, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        Check(key, lifetime);
        ArgumentNullException.ThrowIfNull(value);

        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        return await connection.GetDatabase().StringSetAsync(_prefix + key, value, lifetime, When.NotExists).WaitAsync(cancellationToken);
    }

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> for <paramref name="lifetime"/>, replacing what was there.</summary>
    public async Task SetAsync(string key, byte[] value, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        Check(key, lifetime);
        ArgumentNullException.ThrowIfNull(value);

        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        await connection.GetDatabase().StringSetAsync(_prefix + key, value, lifetime).WaitAsync(cancellationToken);
    }

    /// <summary>The value under <paramref name="key"/>, or <see langword="null"/> when there is none.</summary>
    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        return (byte[]?)await connection.GetDatabase().StringGetAsync(_prefix + key).WaitAsync(cancellationToken);
    }

    /// <summary>Deletes <paramref name="key"/> (one atomic delete); <see langword="true"/> only when it existed.</summary>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        return await connection.GetDatabase().KeyDeleteAsync(_prefix + key).WaitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static void Check(string key, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
    }
}
