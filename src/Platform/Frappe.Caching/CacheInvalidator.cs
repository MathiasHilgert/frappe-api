using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Frappe.Caching;

/// <summary>
/// Invalidates a cache tag after a change is committed, and once more a moment later. A reader that read the old
/// data just before the commit may cache it right after the first invalidation; the second one evicts that copy.
/// The delayed run uses <see cref="TimeProvider"/> timers owned by this singleton: they die with the process and are
/// disposed with the container, and a failure is logged. Anything that still slips through is bounded by the entry
/// duration.
/// </summary>
public sealed partial class CacheInvalidator(IFusionCache cache, TimeProvider time, ILogger<CacheInvalidator> logger) : IAsyncDisposable
{
    /// <summary>Longer than reading one row and writing it to the cache; short enough that nobody notices the stale copy.</summary>
    public static readonly TimeSpan SecondInvalidationDelay = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<ITimer, byte> _pending = new();

    public async Task InvalidateAsync(string tag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        await cache.RemoveByTagAsync(tag, token: cancellationToken);

        // Created disarmed and tracked first, then armed: the callback can never run before the timer is tracked.
        ITimer? timer = null;
        timer = time.CreateTimer(
            state => _ = InvalidateAgainAsync(tag, timer!),
            state: null,
            dueTime: Timeout.InfiniteTimeSpan,
            period: Timeout.InfiniteTimeSpan);
        _pending.TryAdd(timer, 0);
        timer.Change(SecondInvalidationDelay, Timeout.InfiniteTimeSpan);
    }

    private async Task InvalidateAgainAsync(string tag, ITimer timer)
    {
        try
        {
            await cache.RemoveByTagAsync(tag);
        }
        catch (Exception exception)
        {
            LogSecondInvalidationFailed(logger, exception, tag);
        }
        finally
        {
            if (_pending.TryRemove(timer, out _))
            {
                await timer.DisposeAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var timer in _pending.Keys)
        {
            await timer.DisposeAsync();
        }

        _pending.Clear();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The delayed invalidation of cache tag {Tag} failed; the entry duration bounds the stale copy")]
    private static partial void LogSecondInvalidationFailed(ILogger logger, Exception exception, string tag);
}
