namespace Frappe.Caching;

/// <summary>
/// Whether a permit was taken from every window, or which one refused (its index) and the time until it closes; when
/// taken, the time until the first window closes.
/// </summary>
public readonly record struct WindowTakeAll(int? RefusedBy, TimeSpan Remaining)
{
    /// <summary>Whether one permit was taken from every window.</summary>
    public bool Taken => RefusedBy is null;
}
