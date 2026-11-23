namespace Frappe.Http;

/// <summary>How many requests one client may make per window on anonymous endpoints.</summary>
public sealed record AnonymousRateLimit(int PermitLimit, TimeSpan Window)
{
    public static AnonymousRateLimit Default { get; } = new(120, TimeSpan.FromMinutes(1));
}
