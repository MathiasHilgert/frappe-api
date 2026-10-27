using Frappe.Kernel.Domain;

namespace Frappe.Kernel.Messaging;

/// <summary>
/// Publishes events through the outbox of the current transaction.
/// </summary>
public interface IEventPublisher
{
    /// <summary>Publishes a domain event that no tracked aggregate raised (after a guarded write).</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes once the event is in the outbox.</returns>
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct);

    /// <summary>Publishes an integration event to other modules.</summary>
    /// <param name="integrationEvent">The event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes once the event is in the outbox.</returns>
    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct);
}
