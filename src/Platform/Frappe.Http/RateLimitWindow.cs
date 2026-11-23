namespace Frappe.Http;

/// <summary>A window's count so far, and the time until it closes.</summary>
public readonly record struct RateLimitWindow(long Count, TimeSpan Remaining);
