namespace Frappe.Kernel.Domain;

/// <summary>
/// The non-generic view of an aggregate root, for infrastructure that does not know its ID type.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>Gets the domain events raised since they were last cleared.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Gets the concurrency token, which every mutation increments; it is also the ETag.</summary>
    public int Revision { get; }

    /// <summary>Discards the raised domain events once they have been handed to the outbox.</summary>
    public void ClearDomainEvents();
}
