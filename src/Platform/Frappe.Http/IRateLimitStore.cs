namespace Frappe.Http;

/// <summary>
/// Where every replica counts the same rate limit windows, so a client gets one limit no matter which replica answers.
/// Without one, each replica counts on its own. A host registers it as a singleton.
/// </summary>
public interface IRateLimitStore
{
    /// <summary>
    /// Adds <paramref name="permits"/> to <paramref name="key"/>'s window, which opens on its first hit and lasts
    /// <paramref name="window"/> (later hits never extend it), and answers the count so far and the time until it closes.
    /// </summary>
    ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken);
}
