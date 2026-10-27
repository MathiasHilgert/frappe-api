using System.Collections.ObjectModel;

namespace Frappe.Kernel.Domain;

/// <summary>
/// The entry point of an aggregate: it guards the aggregate's invariants, raises its domain events
/// and carries its concurrency token.
/// </summary>
/// <typeparam name="TId">The aggregate's strongly typed ID.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : struct, IEntityId<TId>
{
    private readonly Collection<IDomainEvent> _domainEvents = [];

    /// <summary>Initializes an aggregate root with its identity.</summary>
    /// <param name="id">The identity; never <see langword="default"/>.</param>
    protected AggregateRoot(TId id)
        : base(id) => DomainEvents = _domainEvents.AsReadOnly();

    /// <summary>Initializes an aggregate root for materialization by the persistence layer.</summary>
    protected AggregateRoot() => DomainEvents = _domainEvents.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <inheritdoc />
    public int Revision { get; private set; }

    /// <inheritdoc />
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Records a domain event to publish when the transaction commits.</summary>
    /// <param name="domainEvent">The business fact that happened.</param>
    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Increments <see cref="Revision"/>; every mutating method calls it, so a change to a child
    /// entity alone still conflicts with a concurrent edit of the same aggregate.
    /// </summary>
    protected void Touch() => Revision++;
}
