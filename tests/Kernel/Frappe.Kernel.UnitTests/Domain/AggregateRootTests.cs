namespace Frappe.Kernel.UnitTests.Domain;

public sealed class AggregateRootTests
{
    [Fact]
    public void A_mutation_raises_its_event_and_leaves_the_revision_to_persistence()
    {
        Widget widget = new(WidgetId.NewId());

        widget.Rename();

        widget.DomainEvents.ShouldBe([new WidgetRenamed(widget.Id)]);
        widget.Revision.ShouldBe(0);
    }

    [Fact]
    public void Clearing_discards_the_raised_events()
    {
        Widget widget = new(WidgetId.NewId());
        widget.Rename();

        widget.ClearDomainEvents();

        widget.DomainEvents.ShouldBeEmpty();
    }
}
