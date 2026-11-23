using Frappe.Aggregates;

namespace Frappe.Platform.UnitTests.Aggregates;

/// <summary>The aggregate base class: how it collects domain events and hands them over once.</summary>
public sealed class AggregateRootTests
{
    /// <summary>A domain event the sample aggregate raises when it is renamed.</summary>
    private sealed record Renamed(string Name) : IDomainEvent;

    /// <summary>An aggregate that raises one event when renamed.</summary>
    private sealed class Sample : AggregateRoot
    {
        public void Rename(string name) => Raise(new Renamed(name));
    }

    [Test]
    public async Task Raised_events_are_kept_until_dequeued_once()
    {
        // Arrange
        var sample = new Sample();
        sample.Rename("a");
        sample.Rename("b");

        var first = sample.DequeueDomainEvents();

        // Act
        var second = sample.DequeueDomainEvents();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEquivalentTo(new IDomainEvent[] { new Renamed("a"), new Renamed("b") });
            await Assert.That(second).IsEmpty();
            await Assert.That(sample.DomainEvents).IsEmpty();
        }
    }
}
