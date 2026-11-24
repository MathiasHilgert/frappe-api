using Frappe.IntegrationEvents;

namespace Frappe.Messaging.Abstractions;

/// <summary>A step of a consumer that runs after the inbox transaction committed.</summary>
/// <param name="ConsumerName">The durable name of the consumer that runs the step.</param>
internal sealed record AfterCommitHandlerDeclaration<TEvent, THandler>(string ConsumerName) : IMessagingDeclaration
    where TEvent : IIntegrationEvent
    where THandler : class, IAfterCommitHandler<TEvent>
{
    public void Accept(IMessagingDeclarationVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.AfterCommit<TEvent, THandler>(this);
    }
}
