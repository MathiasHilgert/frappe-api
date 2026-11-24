using System.Reflection;
using Frappe.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Messaging.Abstractions;

/// <summary>
/// How a module declares its messaging without naming a broker: its outbox relay, its consumers with their steps and its message
/// purge. Each call only records what was declared in the service collection. A host that adds a transport (the Worker calls
/// <c>AddJetStream</c>) materializes the declarations, in either order of the calls; a host without one (the Api, the Migrator) runs
/// none, so one module <c>IModule</c> serves every host.
/// </summary>
public static class MessagingDeclarations
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Declares that <paramref name="module"/>'s outbox in <typeparamref name="TContext"/> is published continuously into the
        /// module's own stream. Its options are named after the module and bound from <paramref name="section"/> (the transport's
        /// default when <see langword="null"/>).
        /// </summary>
        public IServiceCollection AddOutboxRelay<TContext>(string module, string? section = null)
            where TContext : DbContext
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(module);
            MessagingTransport.Declare(services, new OutboxRelayDeclaration<TContext>(module, section));
            return services;
        }

        /// <summary>
        /// Declares the purge of <paramref name="module"/>'s outbox and inbox in <typeparamref name="TContext"/>, run by the module's
        /// scheduled job. Its retention options are bound from <paramref name="section"/> (the transport's default when null).
        /// </summary>
        public IServiceCollection AddMessagePurge<TContext>(string module, string? section = null)
            where TContext : DbContext
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(module);
            MessagingTransport.Declare(services, new MessagePurgeDeclaration<TContext>(module, section));
            return services;
        }

        /// <summary>
        /// Declares a consumer of one integration event type from a producer module's stream, through an inbox in
        /// <typeparamref name="TContext"/>'s schema. <paramref name="consumerName"/> is the durable name,
        /// <c>&lt;module&gt;-&lt;purpose&gt;</c>, and is forever: renaming it redelivers the retained history into the inbox.
        /// </summary>
        public IServiceCollection AddIntegrationEventConsumer<TContext, TEvent>(string producerModule,
            string consumerName,
            string? section = null)
            where TContext : DbContext
            where TEvent : IIntegrationEvent
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(producerModule);
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);

            // Fail at declaration, not with a TypeInitializationException the first time a message arrives.
            if (typeof(TEvent).GetCustomAttribute<IntegrationEventAttribute>() is null)
            {
                throw new InvalidOperationException($"{typeof(TEvent).Name} has no [IntegrationEvent(\"<type>\")] attribute and cannot be consumed.");
            }

            MessagingTransport.Declare(services, new IntegrationEventConsumerDeclaration<TContext, TEvent>(producerModule, consumerName, section));
            return services;
        }

        /// <summary>
        /// Declares the step <paramref name="consumerName"/> runs before it claims a message, outside any transaction (a call to
        /// another system, see <see cref="IBeforeClaimHandler{TEvent}"/>); at most one per consumer.
        /// </summary>
        public IServiceCollection AddBeforeClaimHandler<TEvent, THandler>(string consumerName)
            where TEvent : IIntegrationEvent
            where THandler : class, IBeforeClaimHandler<TEvent>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
            MessagingTransport.Declare(services, new BeforeClaimHandlerDeclaration<TEvent, THandler>(consumerName));
            return services;
        }

        /// <summary>Declares the handler <paramref name="consumerName"/> runs in its inbox transaction; at most one per consumer.</summary>
        public IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(string consumerName)
            where TEvent : IIntegrationEvent
            where THandler : class, IIntegrationEventHandler<TEvent>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
            MessagingTransport.Declare(services, new IntegrationEventHandlerDeclaration<TEvent, THandler>(consumerName));
            return services;
        }

        /// <summary>Declares a step <paramref name="consumerName"/> runs after its inbox transaction committed (cache invalidation).</summary>
        public IServiceCollection AddAfterCommitHandler<TEvent, THandler>(string consumerName)
            where TEvent : IIntegrationEvent
            where THandler : class, IAfterCommitHandler<TEvent>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
            MessagingTransport.Declare(services, new AfterCommitHandlerDeclaration<TEvent, THandler>(consumerName));
            return services;
        }
    }
}
