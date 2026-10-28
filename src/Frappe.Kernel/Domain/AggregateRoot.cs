using System.Collections.ObjectModel;

namespace Frappe.Kernel.Domain;

/// <summary>
/// The entry point of an aggregate: it guards the aggregate's invariants, raises its domain events
/// and carries its concurrency token, which persistence increments on every save that changes the
/// aggregate, its owned values or its child entities.
/// </summary>
/// <typeparam name="TId">The aggregate's strongly typed ID.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : struct, IEntityId<TId>
{
    private readonly Collection<IDomainEvent> _domainEvents = [];

    /// <summary>Initializes an aggregate root with its identity.</summary>
    /// <param name="id">The identity; never <see langword="default"/>.</param>
    protected AggregateRoot(TId id)
        : base(id) { }

    /// <summary>Initializes an aggregate root for materialization by the persistence layer.</summary>
    protected AggregateRoot() { }

    /// <inheritdoc />
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

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
}
