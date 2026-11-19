using StackExchange.Redis;

namespace Frappe.Caching;

/// <summary>
/// Fixed window counters in Valkey, shared by every replica: one atomic script adds the hits and opens the window on
/// its first hit (later hits never extend it), under the cache's instance name. Uses its own connection, opened once
/// and opened again on the next hit if that attempt failed.
/// It never waits for Valkey longer than its connection's timeouts; callers bound it further with their token.
/// </summary>
public sealed class ValkeyWindowCounter : IAsyncDisposable
{
    // A key left without an expiry (PTTL -1) gets one too, so no window can outlive its length.
    private const string HitScript = """
        local count = redis.call('INCRBY', KEYS[1], ARGV[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
          redis.call('PEXPIRE', KEYS[1], ARGV[2])
          ttl = tonumber(ARGV[2])
        end
        return { count, ttl }
        """;

    // Takes one permit only while the count is below the limit, in one atomic step, so callers running at once never
    // both take the last one. The window opens on the first permit taken and a key without an expiry gets one.
    private const string TakeScript = """
        local count = tonumber(redis.call('GET', KEYS[1]) or '0')
        local taken = 0
        if count < tonumber(ARGV[1]) then
          count = redis.call('INCR', KEYS[1])
          taken = 1
        end
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
          redis.call('PEXPIRE', KEYS[1], ARGV[2])
          ttl = tonumber(ARGV[2])
        end
        return { taken, count, ttl }
        """;

    // Takes one permit from every window only while each is below its limit (ARGV[i] for KEYS[i]), in one atomic step
    // over all of them: the first window at its limit refuses, naming itself (1-based) and its time left, and nothing is
    // taken. Each window opens on its first permit, and a key without an expiry gets one (ARGV[#KEYS + 1]).
    private const string TakeAllScript = """
        local n = #KEYS
        local window = tonumber(ARGV[n + 1])
        for i = 1, n do
          local count = tonumber(redis.call('GET', KEYS[i]) or '0')
          if count >= tonumber(ARGV[i]) then
            local ttl = redis.call('PTTL', KEYS[i])
            if ttl < 0 then
              redis.call('PEXPIRE', KEYS[i], window)
              ttl = window
            end
            return { i, ttl }
          end
        end
        local shortest = window
        for i = 1, n do
          redis.call('INCR', KEYS[i])
          local ttl = redis.call('PTTL', KEYS[i])
          if ttl < 0 then
            redis.call('PEXPIRE', KEYS[i], window)
            ttl = window
          end
          if ttl < shortest then
            shortest = ttl
          end
        end
        return { 0, shortest }
        """;

    // Gives one permit back, never below zero; the window keeps its expiry, and a closed one stays closed.
    private const string ReturnScript = """
        local count = tonumber(redis.call('GET', KEYS[1]) or '0')
        if count > 0 then
          count = redis.call('DECR', KEYS[1])
        end
        return count
        """;

    private readonly ValkeyConnection _connection;
    private readonly string _prefix;

    internal ValkeyWindowCounter(ConfigurationOptions options, string instanceName)
        : this(() => ConnectionMultiplexer.ConnectAsync(options), instanceName)
    {
    }

    /// <summary>With the connection opened by <paramref name="connect"/>, called again whenever the last attempt failed.</summary>
    internal ValkeyWindowCounter(Func<Task<ConnectionMultiplexer>> connect, string instanceName)
    {
        _connection = new ValkeyConnection(connect);
        _prefix = $"{instanceName}:rate-limit:";
    }

    /// <summary>Adds <paramref name="permits"/> to <paramref name="key"/>'s window; answers its count and the time until it closes.</summary>
    public async Task<WindowCount> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(permits, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        var milliseconds = WindowMilliseconds(window);
        var result = await EvaluateAsync(HitScript, [new RedisKey(_prefix + key)], [permits, milliseconds], cancellationToken);
        return new WindowCount((long)result[0], TimeSpan.FromMilliseconds((long)result[1]));
    }

    /// <summary>
    /// Takes one permit from <paramref name="key"/>'s window only while its count is below <paramref name="limit"/>, in
    /// one atomic step, so callers running at once never take more than <paramref name="limit"/> between them. Answers
    /// whether it was taken, the count after it, and the time until the window closes. For a limit on what fails, take
    /// before trying and <see cref="ReturnAsync"/> after a success.
    /// </summary>
    public async Task<WindowTake> TryTakeAsync(string key, int limit, TimeSpan window, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        var milliseconds = WindowMilliseconds(window);
        var result = await EvaluateAsync(TakeScript, [new RedisKey(_prefix + key)], [limit, milliseconds], cancellationToken);
        return new WindowTake((long)result[0] == 1, (long)result[1], TimeSpan.FromMilliseconds((long)result[2]));
    }

    /// <summary>
    /// Takes one permit from every window of <paramref name="limits"/> only while each is below its limit, in one atomic
    /// script over all their keys, so callers running at once never take more than any limit between them and a refusal
    /// takes from none (nothing to give back). Each window lasts <paramref name="window"/> from its first permit. Answers
    /// which window refused (its index in <paramref name="limits"/>, the first at its limit) and the time until it closes,
    /// or, when taken, the time until the first of them closes. The keys must live on one Valkey node (no cluster slots),
    /// as every key of this counter does, under one prefix. The keys must be distinct, and a repeated key is refused with an
    /// <see cref="ArgumentException"/> that names it: the script checks every key once, against its count before the take,
    /// so a repeated key could end above its limit.
    /// </summary>
    public async Task<WindowTakeAll> TryTakeAllAsync(IReadOnlyList<WindowLimit> limits, TimeSpan window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.Count == 0)
        {
            throw new ArgumentException("At least one window is required.", nameof(limits));
        }

        if (limits.Any(limit => string.IsNullOrEmpty(limit.Key)))
        {
            throw new ArgumentException("Every window needs a key.", nameof(limits));
        }

        if (limits.Any(limit => limit.Limit < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Every window's limit must be at least 1.");
        }

        var duplicate = limits.GroupBy(limit => limit.Key, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"The window '{duplicate.Key}' is named more than once.", nameof(limits));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        var milliseconds = WindowMilliseconds(window);
        RedisKey[] keys = [.. limits.Select(limit => new RedisKey(_prefix + limit.Key))];
        RedisValue[] arguments = [.. limits.Select(limit => (RedisValue)limit.Limit), milliseconds];
        var result = await EvaluateAsync(TakeAllScript, keys, arguments, cancellationToken);
        var refusedBy = (int)result[0];
        return new WindowTakeAll(refusedBy == 0 ? null : refusedBy - 1, TimeSpan.FromMilliseconds((long)result[1]));
    }

    /// <summary>Gives one permit taken from <paramref name="key"/>'s window back, never below zero; answers the count left.</summary>
    public async Task<long> ReturnAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        var reply = await connection.GetDatabase()
            .ScriptEvaluateAsync(ReturnScript, [new RedisKey(_prefix + key)])
            .WaitAsync(cancellationToken);
        return (long)reply;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private async Task<RedisResult[]> EvaluateAsync(string script, RedisKey[] keys, RedisValue[] arguments, CancellationToken cancellationToken)
    {
        var connection = await _connection.GetAsync().WaitAsync(cancellationToken);
        var reply = await connection.GetDatabase()
            .ScriptEvaluateAsync(script, keys, arguments)
            .WaitAsync(cancellationToken);
        return (RedisResult[]?)reply ?? throw new RedisException("The rate limit script answered nothing.");
    }

    private static long WindowMilliseconds(TimeSpan window) =>
        Math.Max(1L, (long)Math.Ceiling(window.TotalMilliseconds));
}
