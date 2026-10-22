namespace Frappe.SharedKernel.Domain;

/// <summary>Consistency boundary. Raised events are dequeued and written to the outbox on save.</summary>
public abstract class AggregateRoot<TId> : IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public TId Id { get; protected init; } = default!;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    IReadOnlyList<IDomainEvent> IHasDomainEvents.DequeueDomainEvents()
    {
        IDomainEvent[] pending = [.. _domainEvents];
        _domainEvents.Clear();
        return pending;
    }
}
