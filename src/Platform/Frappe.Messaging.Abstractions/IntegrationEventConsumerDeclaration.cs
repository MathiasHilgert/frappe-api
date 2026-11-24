using Frappe.IntegrationEvents;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Messaging.Abstractions;

/// <summary>A consumer of <typeparamref name="TEvent"/> from a producer's stream, with its inbox in <typeparamref name="TContext"/>.</summary>
/// <param name="ProducerModule">The module whose stream is consumed.</param>
/// <param name="ConsumerName">The permanent durable name, <c>&lt;module&gt;-&lt;purpose&gt;</c>.</param>
/// <param name="Section">The configuration section of the consumer options, or <see langword="null"/> for the default.</param>
internal sealed record IntegrationEventConsumerDeclaration<TContext, TEvent>(string ProducerModule, string ConsumerName, string? Section)
    : IMessagingDeclaration
    where TContext : DbContext
    where TEvent : IIntegrationEvent
{
    public void Accept(IMessagingDeclarationVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Consumer<TContext, TEvent>(this);
    }
}
