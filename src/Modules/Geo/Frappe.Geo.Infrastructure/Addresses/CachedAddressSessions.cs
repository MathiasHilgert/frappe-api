using Frappe.Geo.Application.Addresses;
using ZiggyCreatures.Caching.Fusion;

namespace Frappe.Geo.Infrastructure.Addresses;

/// <summary>
/// Open search sessions as markers in the platform cache (memory, then Valkey), keyed by provider and session UUID and
/// expiring with the session's lifetime; a marker holds no address and no typed text. The check and the write are not
/// atomic, so two known races over-count: two replicas answering a session's first suggestions at the same moment both
/// open it (started twice), and two validations of one session at the same moment, or before the backplane has evicted
/// a replica's memory copy of a closed marker, both close it (completed twice). Both are rare: a client debounces
/// keystrokes and validates once. Without Valkey the markers fall back to each replica's memory and the counts are per
/// replica.
/// </summary>
internal sealed class CachedAddressSessions(IFusionCache cache) : IAddressSessions
{
    public async Task<bool> OpenAsync(string provider, AddressSession session, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = KeyOf(provider, session);
        if ((await cache.TryGetAsync<bool>(key, Marker(lifetime), cancellationToken)).HasValue)
        {
            return false;
        }

        await cache.SetAsync(key, true, Marker(lifetime), token: cancellationToken);
        return true;
    }

    public async Task<bool> CloseAsync(string provider, AddressSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = KeyOf(provider, session);
        if (!(await cache.TryGetAsync<bool>(key, Marker(TimeSpan.Zero), cancellationToken)).HasValue)
        {
            return false;
        }

        await cache.RemoveAsync(key, Marker(TimeSpan.Zero), cancellationToken);
        return true;
    }

    private static string KeyOf(string provider, AddressSession session) => $"geo:address-session:{provider}:{session.Value}";

    /// <summary>
    /// Exactly the lifetime: no fail-safe (an expired session must stay expired) and no jitter. The Valkey write is
    /// awaited, once per session, so another replica sees the marker as soon as the suggestion is answered. Reads carry
    /// their own 300 ms Valkey bound; an awaited write is not bound by FusionCache's hard timeout, only by the Valkey
    /// client's own (about a second), and the platform's circuit breaker then skips Valkey for 30 s. So an outage costs
    /// a replica's first suggestion or validation up to that second for a metric; FusionCache logs a Valkey failure
    /// (with the key, which holds the session UUID but no address or typed text) and does not throw it.
    /// </summary>
    private static FusionCacheEntryOptions Marker(TimeSpan lifetime) => new()
    {
        Duration = lifetime,
        IsFailSafeEnabled = false,
        JitterMaxDuration = TimeSpan.Zero,
        DistributedCacheSoftTimeout = TimeSpan.FromMilliseconds(100),
        DistributedCacheHardTimeout = TimeSpan.FromMilliseconds(300),
        AllowBackgroundDistributedCacheOperations = false,
    };
}
