using Frappe.IntegrationEvents;
using Frappe.Messaging.Inbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Frappe.Messaging.Hosting;

/// <summary>
/// The steps of one consumer, registered under its durable name so that several modules can consume the same event in
/// one host without running each other's steps. A consumer with steps of its own runs only those, and may have only
/// after-commit steps (no handler): it then just records the message in its inbox and runs them once it is committed. It
/// may also have one before-claim step, which runs outside any transaction before the message is claimed.
/// A consumer with no step of its own keeps the event's single unnamed handler and unnamed after-commit steps.
/// A second handler or the same after-commit step twice fails at registration; a step whose consumer (the same name,
/// case-sensitive, consuming the same event through <see cref="MessagingRegistration.AddIntegrationEventConsumer{TContext, TEvent}"/>)
/// is not registered fails when the host starts.
/// </summary>
public static class ConsumerStepRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// The step <paramref name="consumerName"/> runs before it claims a message, outside any transaction (a call to another
        /// system, see <see cref="IBeforeClaimHandler{TEvent}"/>); at most one per consumer.
        /// </summary>
        public IServiceCollection AddBeforeClaimHandler<TEvent, THandler>(string consumerName)
            where TEvent : IIntegrationEvent
            where THandler : class, IBeforeClaimHandler<TEvent>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
            if (Registered<IBeforeClaimHandler<TEvent>>(services, consumerName).FirstOrDefault() is { } existing)
            {
                throw new InvalidOperationException(
                    $"Consumer '{consumerName}' already has before-claim step {existing.Name} for {typeof(TEvent).Name}; "
                    + $"{typeof(THandler).Name} cannot be added: a consumer has at most one.");
            }

            return services.AddStep<TEvent, THandler>(consumerName).AddKeyedScoped<IBeforeClaimHandler<TEvent>, THandler>(consumerName);
        }

        /// <summary>The handler <paramref name="consumerName"/> runs in its inbox transaction; at most one per consumer.</summary>
        public IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(string consumerName)
            where TEvent : IIntegrationEvent
            where THandler : class, IIntegrationEventHandler<TEvent>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
            if (Registered<IIntegrationEventHandler<TEvent>>(services, consumerName).FirstOrDefault() is { } existing)
            {
                throw new InvalidOperationException(
                    $"Consumer '{consumerName}' already has handler {existing.Name} for {typeof(TEvent).Name}; "
                    + $"{typeof(THandler).Name} cannot be added: a consumer has at most one handler.");
            }

            return services.AddStep<TEvent, THandler>(consumerName).AddKeyedScoped<IIntegrationEventHandler<TEvent>, THandler>(consumerName);
        }

        /// <summary>A step <paramref name="consumerName"/> runs after its inbox transaction committed (cache invalidation).</summary>
        public IServiceCollection AddAfterCommitHandler<TEvent, THandler>(string consumerName)
            where TEvent : IIntegrationEvent
            where THandler : class, IAfterCommitHandler<TEvent>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
            if (Registered<IAfterCommitHandler<TEvent>>(services, consumerName).Contains(typeof(THandler)))
            {
                throw new InvalidOperationException(
                    $"Consumer '{consumerName}' already has after-commit step {typeof(THandler).Name} for {typeof(TEvent).Name}; "
                    + "registering it again would run it twice.");
            }

            return services.AddStep<TEvent, THandler>(consumerName).AddKeyedScoped<IAfterCommitHandler<TEvent>, THandler>(consumerName);
        }

        private IServiceCollection AddStep<TEvent, THandler>(string consumerName)
        {
            services.AddOptions<ConsumerStepOptions>()
                .Configure(options => options.Steps.Add(new ConsumerStep(consumerName, typeof(TEvent), typeof(THandler))))
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ConsumerStepOptions>, ConsumerStepValidator>());
            return services;
        }
    }


    private static IEnumerable<Type> Registered<TService>(IServiceCollection services, string consumerName) =>
        services
            .Where(d => d.IsKeyedService && d.ServiceType == typeof(TService) && Equals(d.ServiceKey, consumerName))
            .Select(d => d.KeyedImplementationType ?? typeof(TService));
}
