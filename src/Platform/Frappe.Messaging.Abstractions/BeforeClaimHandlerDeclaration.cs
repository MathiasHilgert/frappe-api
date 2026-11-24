using Frappe.IntegrationEvents;

namespace Frappe.Messaging.Abstractions;

/// <summary>A step of a consumer that runs before the message is claimed, outside any transaction.</summary>
/// <param name="ConsumerName">The durable name of the consumer that runs the step.</param>
internal sealed record BeforeClaimHandlerDeclaration<TEvent, THandler>(string ConsumerName) : IMessagingDeclaration
    where TEvent : IIntegrationEvent
    where THandler : class, IBeforeClaimHandler<TEvent>
{
    public void Accept(IMessagingDeclarationVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.BeforeClaim<TEvent, THandler>(this);
    }
}
