using Frappe.SharedKernel.Domain;
using Shouldly;

namespace Frappe.SharedKernel.Tests;

public sealed class AggregateRootTests
{
    private sealed record Happened : IDomainEvent;

    private sealed class Sample : AggregateRoot<Guid>
    {
        public void Act() => Raise(new Happened());
    }

    [Fact]
    public void DequeueDomainEvents_ReturnsPendingEventsOnlyOnce()
    {
        var aggregate = new Sample();
        aggregate.Act();
        var events = (IHasDomainEvents)aggregate;

        events.DequeueDomainEvents().Count.ShouldBe(1);
        events.DequeueDomainEvents().ShouldBeEmpty();
    }
}
