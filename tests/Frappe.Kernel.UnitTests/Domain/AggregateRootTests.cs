namespace Frappe.Kernel.UnitTests.Domain;

public sealed class AggregateRootTests
{
    [Fact]
    public void A_mutation_raises_its_event_and_increments_the_revision()
    {
        Widget widget = new(WidgetId.NewId());

        widget.Rename();

        widget.DomainEvents.ShouldBe([new WidgetRenamed(widget.Id)]);
        widget.Revision.ShouldBe(1);
    }

    [Fact]
    public void Clearing_discards_the_raised_events_but_keeps_the_revision()
    {
        Widget widget = new(WidgetId.NewId());
        widget.Rename();

        widget.ClearDomainEvents();

        widget.DomainEvents.ShouldBeEmpty();
        widget.Revision.ShouldBe(1);
    }
}
