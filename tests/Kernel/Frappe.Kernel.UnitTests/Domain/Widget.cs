using Frappe.Kernel.Domain;

namespace Frappe.Kernel.UnitTests.Domain;

internal sealed class Widget : AggregateRoot<WidgetId>
{
    public Widget(WidgetId id)
        : base(id) { }

    private Widget() { }

    // Stands in for the persistence layer, which materializes through the parameterless constructor.
    public static Widget Materialized() => new();

    public void Rename() => Raise(new WidgetRenamed(Id));
}
