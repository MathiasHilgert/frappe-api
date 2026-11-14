namespace Frappe.Http;

/// <summary>
/// The anonymous rate limit, bound from <see cref="Section"/> (<c>Http:RateLimit:PermitLimit</c>, <c>Window</c>),
/// by default <see cref="AnonymousRateLimit.Default"/>.
/// </summary>
public sealed class AnonymousRateLimitOptions
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Http:RateLimit";

    /// <summary>Requests one client may make per window.</summary>
    public int PermitLimit { get; set; } = AnonymousRateLimit.Default.PermitLimit;

    /// <summary>The fixed window the permits are counted in.</summary>
    public TimeSpan Window { get; set; } = AnonymousRateLimit.Default.Window;
}
