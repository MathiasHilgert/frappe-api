namespace Frappe.Http;

/// <summary>
/// How many requests one client may make per window on anonymous endpoints. The same shape (permits per window)
/// serves every policy added through <c>AddFrappeRateLimitPolicy</c>.
/// </summary>
public sealed record AnonymousRateLimit(int PermitLimit, TimeSpan Window)
{
    /// <summary>120 requests per minute.</summary>
    public static AnonymousRateLimit Default { get; } = new(120, TimeSpan.FromMinutes(1));
}
