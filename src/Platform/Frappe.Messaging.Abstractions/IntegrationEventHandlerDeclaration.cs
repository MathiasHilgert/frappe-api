using Frappe.IntegrationEvents;

namespace Frappe.Messaging.Abstractions;

/// <summary>A step of a consumer that runs in the inbox transaction.</summary>
/// <param name="ConsumerName">The durable name of the consumer that runs the step.</param>
internal sealed record IntegrationEventHandlerDeclaration<TEvent, THandler>(string ConsumerName) : IMessagingDeclaration
    where TEvent : IIntegrationEvent
    where THandler : class, IIntegrationEventHandler<TEvent>
{
    public void Accept(IMessagingDeclarationVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Handler<TEvent, THandler>(this);
    }
}
