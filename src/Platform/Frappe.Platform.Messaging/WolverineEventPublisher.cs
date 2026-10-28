using Frappe.Kernel.Domain;
using Frappe.Kernel.Messaging;
using Wolverine;

namespace Frappe.Platform.Messaging;

/// <summary>
/// <see cref="IEventPublisher"/> on the Wolverine outbox. Public and constructor-injected so a handler gets the
/// one built from its own message context: an event is stored with the handler's transaction and delivered only
/// after it commits.
/// </summary>
/// <param name="bus">The message context of the current handler or scope.</param>
public sealed class WolverineEventPublisher(IMessageContext bus) : IEventPublisher
{
    /// <inheritdoc />
    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        await bus.PublishAsync(domainEvent, WolverineDispatcher.Delivery(bus));
    }

    /// <inheritdoc />
    public async Task PublishAsync(
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken
    )
    {
        // TODO: the NATS PR routes integration events through JetStream (stream FRAPPE_EVENTS, a durable
        // consumer per subscribing module); until then they reach subscribers on durable local queues.
        // TODO: the event log PR writes the events.event_log row here, in the same transaction.
        await bus.PublishAsync(integrationEvent, WolverineDispatcher.Delivery(bus));
    }
}
