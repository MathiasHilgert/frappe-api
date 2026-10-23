namespace Frappe.Identity.Application.Security;

/// <summary>At most <paramref name="Limit"/> attempts per key every <paramref name="Window"/>.</summary>
public sealed record AttemptPolicy(string Name, int Limit, TimeSpan Window);
