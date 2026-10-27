namespace Frappe.Kernel.UnitTests.Domain;

public sealed class EntityTests
{
    [Fact]
    public void Entities_of_the_same_type_with_the_same_id_are_equal()
    {
        var id = WidgetId.NewId();
        Widget first = new(id);
        Widget second = new(id);

        bool equal = first == second;

        equal.ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Entities_with_different_ids_are_not_equal()
    {
        Widget first = new(WidgetId.NewId());
        Widget second = new(WidgetId.NewId());

        bool equal = first == second;

        equal.ShouldBeFalse();
    }

    [Fact]
    public void Entities_of_different_types_with_the_same_id_are_not_equal()
    {
        var id = WidgetId.NewId();
        Widget widget = new(id);
        Gadget gadget = new(id);

        bool equal = widget.Equals(gadget);

        equal.ShouldBeFalse();
    }

    [Fact]
    public void Two_entities_without_an_id_are_not_equal()
    {
        var first = Widget.Materialized();
        var second = Widget.Materialized();

        bool equal = first == second;

        equal.ShouldBeFalse();
    }

    [Fact]
    public void An_entity_without_an_id_equals_itself()
    {
        var widget = Widget.Materialized();

        bool equal = widget.Equals(widget);

        equal.ShouldBeTrue();
    }

    [Fact]
    public void An_entity_cannot_be_created_with_a_default_id()
    {
        Action create = () => _ = new Widget(default);

        create.ShouldThrow<ArgumentException>();
    }
}
