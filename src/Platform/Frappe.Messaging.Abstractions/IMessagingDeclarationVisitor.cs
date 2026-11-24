using Frappe.IntegrationEvents;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Messaging.Abstractions;

/// <summary>The transport that turns declarations into relays, consumers, topology and purgers (Frappe.Messaging, Worker only).</summary>
internal interface IMessagingDeclarationVisitor
{
    void Relay<TContext>(OutboxRelayDeclaration<TContext> declaration)
        where TContext : DbContext;

    void Purge<TContext>(MessagePurgeDeclaration<TContext> declaration)
        where TContext : DbContext;

    void Consumer<TContext, TEvent>(IntegrationEventConsumerDeclaration<TContext, TEvent> declaration)
        where TContext : DbContext
        where TEvent : IIntegrationEvent;

    void BeforeClaim<TEvent, THandler>(BeforeClaimHandlerDeclaration<TEvent, THandler> declaration)
        where TEvent : IIntegrationEvent
        where THandler : class, IBeforeClaimHandler<TEvent>;

    void Handler<TEvent, THandler>(IntegrationEventHandlerDeclaration<TEvent, THandler> declaration)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>;

    void AfterCommit<TEvent, THandler>(AfterCommitHandlerDeclaration<TEvent, THandler> declaration)
        where TEvent : IIntegrationEvent
        where THandler : class, IAfterCommitHandler<TEvent>;
}
