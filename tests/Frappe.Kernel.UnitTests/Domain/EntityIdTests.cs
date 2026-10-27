using Frappe.Kernel.Domain;

namespace Frappe.Kernel.UnitTests.Domain;

public sealed class EntityIdTests
{
    [Fact]
    public void A_UUIDv7_is_accepted()
    {
        var value = Guid.CreateVersion7();

        ErrorOr<WidgetId> result = WidgetId.From(value);

        result.Value.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301")]
    public void An_empty_or_non_v7_value_is_rejected(string raw)
    {
        ErrorOr<WidgetId> result = WidgetId.From(Guid.Parse(raw));

        result.FirstError.ShouldBe(EntityId.Invalid);
    }

    [Fact]
    public void Each_new_id_is_a_distinct_UUIDv7()
    {
        var first = WidgetId.NewId();

        var second = WidgetId.NewId();

        first.ShouldNotBe(second);
        first.Value.Version.ShouldBe(7);
    }
}
