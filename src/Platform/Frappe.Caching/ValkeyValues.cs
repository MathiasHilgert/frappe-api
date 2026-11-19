using StackExchange.Redis;

namespace Frappe.Caching;

/// <summary>
/// Small values in Valkey, shared by every replica, under the cache's instance name: written only if absent, read,
/// overwritten and deleted, also only while they still hold an expected value, each in one atomic command, and every
/// value expires on its own. Not a cache: a value lost to
/// an outage is not rebuilt from anywhere (the platform's <c>Idempotency-Key</c> answers live here). Uses its own
/// connection, opened once and opened again on the next call if that attempt failed. It never waits for Valkey longer
/// than its connection's timeouts; callers bound it further with their token.
/// </summary>
public sealed class ValkeyValues : IAsyncDisposable
{
    /// <summary>KEYS[1] the key; ARGV[1] the value it must hold, ARGV[2] the new value, ARGV[3] its lifetime in milliseconds.</summary>
    private const string ReplaceIfScript = """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
        redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
        return 1
        """;

    /// <summary>KEYS[1] the key; ARGV[1] the value it must hold.</summary>
    private const string DeleteIfScript = """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
        return redis.call('DEL', KEYS[1])
        """;

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
        _prefix = $"{instanceName}:";
    }

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> for <paramref name="lifetime"/> only if the key is absent; <see langword="true"/> when it was stored.</summary>
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

    /// <summary>
    /// Replaces the value under <paramref name="key"/> with <paramref name="value"/> for <paramref name="lifetime"/>, in one
    /// atomic script, only while it still holds exactly <paramref name="expected"/>; <see langword="true"/> when replaced.
    /// </summary>
    public async Task<bool> ReplaceIfAsync(string key, byte[] expected, byte[] value, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        Check(key, lifetime);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(value);

        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        var reply = await connection.GetDatabase()
            .ScriptEvaluateAsync(ReplaceIfScript, [new RedisKey(_prefix + key)], [expected, value, (long)Math.Ceiling(lifetime.TotalMilliseconds)])
            .WaitAsync(cancellationToken);
        return (long)reply == 1;
    }

    /// <summary>
    /// Deletes <paramref name="key"/>, in one atomic script, only while it still holds exactly <paramref name="expected"/>;
    /// <see langword="true"/> when deleted.
    /// </summary>
    public async Task<bool> DeleteIfAsync(string key, byte[] expected, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(expected);

        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        var reply = await connection.GetDatabase()
            .ScriptEvaluateAsync(DeleteIfScript, [new RedisKey(_prefix + key)], [expected])
            .WaitAsync(cancellationToken);
        return (long)reply == 1;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static void Check(string key, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
    }
}
