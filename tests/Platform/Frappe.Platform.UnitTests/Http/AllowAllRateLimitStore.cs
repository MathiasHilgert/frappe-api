using Frappe.Http;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>A shared rate limit store that never refuses: for tests of a host registered from configuration, which has no Valkey.</summary>
internal sealed class AllowAllRateLimitStore : IRateLimitStore
{
    public ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new RateLimitWindow(permits, window));
}
