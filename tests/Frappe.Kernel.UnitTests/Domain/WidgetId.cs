using Frappe.Kernel.Domain;

namespace Frappe.Kernel.UnitTests.Domain;

internal readonly record struct WidgetId : IEntityId<WidgetId>
{
    private WidgetId(Guid value) => Value = value;

    public Guid Value { get; }

    public static WidgetId NewId() => new(Guid.CreateVersion7());

    public static ErrorOr<WidgetId> From(Guid value) =>
        EntityId.Validate(value).Then(static v => new WidgetId(v));
}
