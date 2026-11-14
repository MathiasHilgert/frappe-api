namespace Frappe.Aggregates;

/// <summary>Something that happened inside an aggregate and that other parts of the system may react to.</summary>
public interface IDomainEvent;

/// <summary>
/// Consistency boundary that records what happened to it. Events stay in the aggregate until the
/// unit of work dequeues them, so they are published only if the state change is committed.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Returns the pending events and forgets them, so each event is handed over exactly once.</summary>
    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
