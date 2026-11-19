namespace Frappe.Http;

/// <summary>
/// The anonymous rate limit, bound from <see cref="Section"/> (<c>Http:RateLimit:PermitLimit</c>, <c>Window</c>),
/// by default <see cref="AnonymousRateLimit.Default"/>. The same shape (permits per window) is bound for every policy
/// added through <c>AddFrappeRateLimitPolicy</c>.
/// </summary>
public sealed class AnonymousRateLimitOptions
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Http:RateLimit";

    /// <summary>
    /// <c>Http:RateLimit:PermitLimit</c>: requests one client address may make in one <see cref="Window"/> before
    /// it is answered 429. Default 120. Range 1 or more (a policy of its own, such as
    /// <c>&lt;Module&gt;:&lt;Feature&gt;:RateLimit</c>, binds the same name from its own section with its own default). Raising it
    /// lets a client do more work per window; lowering it refuses legitimate bursts sooner.
    /// </summary>
    public int PermitLimit { get; set; } = AnonymousRateLimit.Default.PermitLimit;

    /// <summary>
    /// <c>Http:RateLimit:Window</c>: the fixed window the permits are counted in, a time span such as
    /// <c>00:01:00</c>. Default one minute. Range greater than zero. A longer window with the same permits lowers
    /// the sustained rate a client may keep, and lengthens the <c>Retry-After</c> a refused client is told.
    /// </summary>
    public TimeSpan Window { get; set; } = AnonymousRateLimit.Default.Window;
}
