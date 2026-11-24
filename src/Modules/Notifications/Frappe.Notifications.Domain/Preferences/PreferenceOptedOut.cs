using Frappe.Aggregates;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Domain.Preferences;

/// <summary>A recipient turned a category off. Counted once the commit lands (S15); it carries no recipient (S13).</summary>
/// <param name="Category">What they turned off.</param>
public sealed record PreferenceOptedOut(NotificationCategory Category) : IDomainEvent;
