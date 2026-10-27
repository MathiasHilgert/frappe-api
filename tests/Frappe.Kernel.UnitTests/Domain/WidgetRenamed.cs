using Frappe.Kernel.Domain;

namespace Frappe.Kernel.UnitTests.Domain;

internal sealed record WidgetRenamed(WidgetId WidgetId) : IDomainEvent;
