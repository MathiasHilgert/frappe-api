using System.Collections.Concurrent;

namespace Frappe.Http;

/// <summary>
/// The rate limit windows of one process: what a host gets when it registers no store every replica shares. A window opens
/// on its first hit and closes <c>window</c> later; windows that closed are dropped as new hits arrive.
/// </summary>
internal sealed class InMemoryRateLimitStore(TimeProvider time) : IRateLimitStore
{
    private static readonly long SweepEveryTicks = TimeSpan.FromMinutes(1).Ticks;

    private readonly ConcurrentDictionary<string, Window> _windows = new();
    private long _nextSweep;

    public ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcTicks;
        Sweep(now);
        var current = _windows.AddOrUpdate(
            key,
            _ => new Window(permits, now + window.Ticks),
            (_, existing) => existing.EndsAt <= now
                ? new Window(permits, now + window.Ticks)
                : existing with { Count = existing.Count + permits });
        return ValueTask.FromResult(new RateLimitWindow(current.Count, TimeSpan.FromTicks(current.EndsAt - now)));
    }

    private void Sweep(long now)
    {
        var due = Volatile.Read(ref _nextSweep);
        if (now < due || Interlocked.CompareExchange(ref _nextSweep, now + SweepEveryTicks, due) != due)
        {
            return;
        }

        foreach (var (key, window) in _windows)
        {
            if (window.EndsAt <= now)
            {
                _windows.TryRemove(new KeyValuePair<string, Window>(key, window));
            }
        }
    }

    /// <summary>One key's count so far and the tick its window closes at.</summary>
    private sealed record Window(long Count, long EndsAt);
}
