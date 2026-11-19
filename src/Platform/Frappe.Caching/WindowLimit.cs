namespace Frappe.Caching;

/// <summary>One window of <see cref="ValkeyWindowCounter.TryTakeAllAsync"/>: its key and how many permits it holds.</summary>
public readonly record struct WindowLimit(string Key, int Limit);
