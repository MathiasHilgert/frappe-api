using System.Threading.RateLimiting;

namespace Frappe.Http;

/// <summary>
/// One client's fixed window, counted in the <see cref="SharedRateLimitStore"/> every replica shares, with no queue.
/// While the store is unavailable it counts in its own in-memory window of the same size, which the partitioned
/// limiter replenishes (<see cref="IsAutoReplenishing"/> is <see langword="false"/>). The store is only asked
/// asynchronously: <see cref="AttemptAcquireCore"/>, which the middleware tries first, never grants a permit.
/// </summary>
internal sealed class SharedFixedWindowRateLimiter : ReplenishingRateLimiter
{
    private readonly string _key;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly SharedRateLimitStore _store;
    private readonly TimeProvider _time;
    private readonly FixedWindowRateLimiter _fallback;
    private long _lastUsed;

    public SharedFixedWindowRateLimiter(string key, int permitLimit, TimeSpan window, SharedRateLimitStore store, TimeProvider time)
    {
        _key = key;
        _permitLimit = permitLimit;
        _window = window;
        _store = store;
        _time = time;
        _fallback = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = false,
        });
        _lastUsed = time.GetTimestamp();
    }

    /// <summary>Idle since its last request, unless its fallback window still holds permits.</summary>
    public override TimeSpan? IdleDuration =>
        _fallback.IdleDuration is null ? null : _time.GetElapsedTime(Interlocked.Read(ref _lastUsed));

    public override bool IsAutoReplenishing => false;

    public override TimeSpan ReplenishmentPeriod => _fallback.ReplenishmentPeriod;

    public override bool TryReplenish() => _fallback.TryReplenish();

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
        permitCount == 0 ? WindowLease.Acquired : WindowLease.NotCounted;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        if (permitCount == 0)
        {
            return WindowLease.Acquired;
        }

        Interlocked.Exchange(ref _lastUsed, _time.GetTimestamp());
        var window = await _store.TryHitAsync(_key, permitCount, _window, cancellationToken);
        if (window is not { } shared)
        {
            return _fallback.AttemptAcquire(permitCount);
        }

        return shared.Count <= _permitLimit ? WindowLease.Acquired : WindowLease.Rejected(shared.Remaining);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fallback.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _fallback.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    /// <summary>The limiter's answer for one request: counted and allowed, counted and refused with a wait, or not counted at all.</summary>
    private sealed class WindowLease(bool isAcquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public static WindowLease Acquired { get; } = new(isAcquired: true, retryAfter: null);

        public static WindowLease NotCounted { get; } = new(isAcquired: false, retryAfter: null);

        public static WindowLease Rejected(TimeSpan retryAfter) => new(isAcquired: false, retryAfter);

        public override bool IsAcquired => isAcquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } wait && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = wait;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
