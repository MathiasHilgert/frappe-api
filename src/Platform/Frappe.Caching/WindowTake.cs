namespace Frappe.Caching;

/// <summary>Whether a permit was taken, the window's count after it, and the time until the window closes.</summary>
public readonly record struct WindowTake(bool Taken, long Count, TimeSpan Remaining);
