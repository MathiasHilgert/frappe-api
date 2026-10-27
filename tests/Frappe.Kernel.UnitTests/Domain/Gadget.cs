using Frappe.Kernel.Domain;

namespace Frappe.Kernel.UnitTests.Domain;

internal sealed class Gadget(WidgetId id) : Entity<WidgetId>(id);
