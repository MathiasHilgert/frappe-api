namespace Frappe.Kernel.Domain;

/// <summary>
/// The non-generic view of an aggregate root, for infrastructure that does not know its ID type.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>Gets the domain events raised since they were last cleared.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>
    /// Gets the concurrency token and ETag; persistence increments it once per save that changes the
    /// aggregate, so domain code never sets it.
    /// </summary>
    public int Revision { get; }

    /// <summary>Discards the raised domain events once they have been handed to the outbox.</summary>
    public void ClearDomainEvents();
}
