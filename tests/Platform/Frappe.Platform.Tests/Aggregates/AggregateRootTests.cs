using Frappe.Aggregates;

namespace Frappe.Platform.Tests.Aggregates;

public sealed class AggregateRootTests
{
    private sealed record Renamed(string Name) : IDomainEvent;

    private sealed class Sample : AggregateRoot
    {
        public void Rename(string name) => Raise(new Renamed(name));
    }

    [Test]
    public async Task Raised_events_are_kept_until_dequeued_once()
    {
        var sample = new Sample();
        sample.Rename("a");
        sample.Rename("b");

        var first = sample.DequeueDomainEvents();
        var second = sample.DequeueDomainEvents();

        await Assert.That(first).IsEquivalentTo(new IDomainEvent[] { new Renamed("a"), new Renamed("b") });
        await Assert.That(second).IsEmpty();
        await Assert.That(sample.DomainEvents).IsEmpty();
    }
}
