using Frappe.Kernel.Domain;

namespace Frappe.Kernel.UnitTests.Domain;

internal sealed class Widget(WidgetId id) : AggregateRoot<WidgetId>(id)
{
    public void Rename()
    {
        Touch();
        Raise(new WidgetRenamed(Id));
    }
}
