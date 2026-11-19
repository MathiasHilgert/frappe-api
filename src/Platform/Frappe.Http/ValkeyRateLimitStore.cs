using Frappe.Caching;

namespace Frappe.Http;

/// <summary>The rate limit windows every replica of the host shares, counted in Valkey.</summary>
internal sealed class ValkeyRateLimitStore(ValkeyWindowCounter counter) : IRateLimitStore
{
    public async ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
    {
        var count = await counter.HitAsync(key, permits, window, cancellationToken);
        return new RateLimitWindow(count.Count, count.Remaining);
    }
}
