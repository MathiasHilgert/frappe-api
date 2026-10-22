namespace Frappe.SharedKernel.Domain;

/// <summary>Lets the persistence layer collect raised events exactly once, when it saves the aggregate.</summary>
public interface IHasDomainEvents
{
    /// <summary>Returns the pending events and forgets them, so a second save never republishes them.</summary>
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();
}
