using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Security;
using Frappe.Platform.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// Fixed-window counters in Valkey. One Lua script increments and starts the window atomically, so concurrent
/// attempts never lose the expiry. Keys hold a SHA-256 of the lower-cased key, never the raw email or IP address.
/// Valkey is a non-critical dependency: when it is down, or slower than the limiter's own per-call budget
/// (<see cref="AttemptLimiterOptions"/>), every operation fails open (logged and counted), because
/// locking every owner out during a cache outage is worse than briefly losing the throttle.
/// </summary>
public sealed partial class ValkeyAttemptLimiter(
    ValkeyConnection valkey,
    IOptions<AttemptLimiterOptions> options,
    IdentityMetrics metrics,
    ILogger<ValkeyAttemptLimiter> logger) : IAttemptLimiter
{
    private const string KeyPrefix = "frappe:identity:attempts:";

    private static readonly LuaScript Increment = LuaScript.Prepare(
        """
        local count = redis.call('INCR', @key)
        if count == 1 then redis.call('PEXPIRE', @key, @windowMs) end
        return { count, redis.call('PTTL', @key) }
        """);

    // Refuses without counting once the key is at the limit, so a burst of parallel attempts cannot overshoot it.
    private static readonly LuaScript Reserve = LuaScript.Prepare(
        """
        local count = tonumber(redis.call('GET', @key) or '0')
        if count >= tonumber(@limit) then return { 0, count, redis.call('PTTL', @key) } end
        count = redis.call('INCR', @key)
        if count == 1 then redis.call('PEXPIRE', @key, @windowMs) end
        return { 1, count, redis.call('PTTL', @key) }
        """);

    // DECR keeps the window's expiry; a key that already expired is not resurrected.
    private static readonly LuaScript Release = LuaScript.Prepare(
        """
        local count = tonumber(redis.call('GET', @key) or '0')
        if count > 0 then redis.call('DECR', @key) end
        return 0
        """);

    public async Task<AttemptDecision> RecordAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        using var budget = Budget(cancellationToken);
        try
        {
            var database = await DatabaseAsync(budget.Token);
            var reply = await database.ScriptEvaluateAsync(Increment,
                new { key = KeyFor(policy, key), windowMs = (long)policy.Window.TotalMilliseconds }).WaitAsync(budget.Token);
            var values = (long[]?)reply ?? throw new RedisException("The attempt script returned no counters.");
            var (count, ttlMs) = (values[0], values[1]);
            return Decide(policy, count, count <= policy.Limit, ttlMs > 0 ? TimeSpan.FromMilliseconds(ttlMs) : null);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            return Skip(policy, exception);
        }
    }

    public async Task<AttemptDecision> ReserveAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        using var budget = Budget(cancellationToken);
        try
        {
            var database = await DatabaseAsync(budget.Token);
            var reply = await database.ScriptEvaluateAsync(Reserve, new
            {
                key = KeyFor(policy, key),
                limit = policy.Limit,
                windowMs = (long)policy.Window.TotalMilliseconds,
            }).WaitAsync(budget.Token);
            var values = (long[]?)reply ?? throw new RedisException("The reserve script returned no counters.");
            var (reserved, count, ttlMs) = (values[0] == 1, values[1], values[2]);
            return Decide(policy, count, reserved, ttlMs > 0 ? TimeSpan.FromMilliseconds(ttlMs) : null);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            return Skip(policy, exception);
        }
    }

    public async Task ReleaseAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        using var budget = Budget(cancellationToken);
        try
        {
            var database = await DatabaseAsync(budget.Token);
            await database.ScriptEvaluateAsync(Release, new { key = KeyFor(policy, key) }).WaitAsync(budget.Token);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            Skip(policy, exception);
        }
    }

    public async Task ResetAsync(AttemptPolicy policy, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        using var budget = Budget(cancellationToken);
        try
        {
            var database = await DatabaseAsync(budget.Token);
            await database.KeyDeleteAsync(KeyFor(policy, key)).WaitAsync(budget.Token);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            Skip(policy, exception);
        }
    }

    private static AttemptDecision Decide(AttemptPolicy policy, long count, bool allowed, TimeSpan? ttl)
    {
        var remaining = (int)Math.Max(0, policy.Limit - count);
        if (allowed)
            return AttemptDecision.Allow(remaining);

        // A key without a TTL cannot outlive its window by design; fall back to the full window if one ever does.
        var retryAfter = ttl is { } left && left > TimeSpan.Zero ? left : policy.Window;
        return new AttemptDecision(false, remaining, retryAfter);
    }

    private static RedisKey KeyFor(AttemptPolicy policy, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim().ToLowerInvariant()));
        return $"{KeyPrefix}{policy.Name}:{Convert.ToHexStringLower(digest)}";
    }

    private static bool IsOutage(Exception exception, CancellationToken cancellationToken) =>
        exception is RedisException or TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private CancellationTokenSource Budget(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMilliseconds(options.Value.TimeoutMilliseconds));
        return budget;
    }

    private async Task<IDatabase> DatabaseAsync(CancellationToken cancellationToken) =>
        (await valkey.GetAsync().WaitAsync(cancellationToken)).GetDatabase();

    private AttemptDecision Skip(AttemptPolicy policy, Exception exception)
    {
        LogLimiterSkipped(exception, policy.Name);
        metrics.AttemptLimiterSkipped();
        return AttemptDecision.Allow(policy.Limit);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Attempt limiter unavailable for {Policy}; allowing the attempt")]
    private partial void LogLimiterSkipped(Exception exception, string policy);
}
