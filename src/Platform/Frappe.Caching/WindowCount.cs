namespace Frappe.Caching;

/// <summary>A window's count so far, and the time until it closes.</summary>
public readonly record struct WindowCount(long Count, TimeSpan Remaining);
