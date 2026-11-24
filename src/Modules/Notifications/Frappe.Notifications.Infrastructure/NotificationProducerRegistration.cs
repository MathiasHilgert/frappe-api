using System.Reflection;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Abstractions;
using Frappe.Notifications.Application.Producers;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Notifications.Infrastructure;

/// <summary>How a producer plug-in (<c>Frappe.Notifications.Producers.&lt;Module&gt;</c>) joins the Worker (S2a).</summary>
public static class NotificationProducerRegistration
{
    /// <param name="services">The producer plug-in's Worker services.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Makes the facts of <typeparamref name="TEvent"/>, an integration event another module publishes, into notifications: declares the
        /// consumer <paramref name="consumerName"/> of the producer's stream, whose before-claim step runs <typeparamref name="TMapper"/> and then
        /// the dispatch pipeline, as the explicit path does. The producer module is the one in the event's type
        /// (<c>frappe.&lt;module&gt;.&lt;fact&gt;.v1</c>), and it knows nothing about Notifications. Like every declaration it runs in a host
        /// that has a transport (the Worker) and is recorded and idle elsewhere.
        /// </summary>
        /// <typeparam name="TEvent">The producer's integration event.</typeparam>
        /// <typeparam name="TMapper">Maps the event to the notifications it asks for.</typeparam>
        /// <param name="consumerName">
        /// The durable consumer name, <c>notifications-&lt;producer&gt;-&lt;purpose&gt;</c>; it is forever: renaming it redelivers the
        /// retained stream.
        /// </param>
        /// <param name="section">
        /// The configuration section of the consumer's delivery, backoff and dead-letter settings; <see cref="DefaultSection"/> when
        /// <see langword="null"/>.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// The event has no integration event type, the consumer name is not Notifications' own, or the event already has a mapper.
        /// </exception>
        public IServiceCollection AddNotificationProducer<TEvent, TMapper>(string consumerName, string? section = null)
            where TEvent : IIntegrationEvent
            where TMapper : class, INotificationProducer<TEvent>
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
            if (!consumerName.StartsWith(ConsumerPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Producer consumer '{consumerName}' must be named '{ConsumerPrefix}<producer>-<purpose>'.");
            }

            if (services.Any(descriptor => descriptor.ServiceType == typeof(INotificationProducer<TEvent>)))
            {
                throw new InvalidOperationException(
                    $"{typeof(TEvent).Name} already has a notification producer: one mapper per event, so a fact cannot be mapped twice.");
            }

            services.TryAddScoped<CurrentMessage>();
            services.TryAddScoped<CatalogDispatch>();
            services.TryAddScoped<ParkedFeedbackApplier>();
            services.AddScoped<INotificationProducer<TEvent>, TMapper>();
            return services
                .AddIntegrationEventConsumer<NotificationsDbContext, TEvent>(
                    ProducerModuleOf<TEvent>(), consumerName, section ?? DefaultSection(consumerName))
                .AddBeforeClaimHandler<TEvent, DispatchProducedNotifications<TEvent>>(consumerName)
                .AddIntegrationEventHandler<TEvent, ApplyParkedFeedbackFor<TEvent>>(consumerName);
        }
    }

    /// <summary>The prefix of every consumer name Notifications owns: <c>notifications-&lt;producer&gt;-&lt;purpose&gt;</c>.</summary>
    public const string ConsumerPrefix = "notifications-";

    /// <summary>The configuration section a producer's consumer reads by default: <c>Notifications:Producers:{consumer name}:Consumer</c>.</summary>
    public static string DefaultSection(string consumerName) => $"Notifications:Producers:{consumerName}:Consumer";

    private static string ProducerModuleOf<TEvent>()
    {
        var type = typeof(TEvent).GetCustomAttribute<IntegrationEventAttribute>()?.Type;
        var segments = type?.Split('.');
        if (segments is not { Length: >= 4 } || segments[0] != "frappe")
        {
            throw new InvalidOperationException(
                $"{typeof(TEvent).Name} has no [IntegrationEvent(\"frappe.<module>.<fact>.v<n>\")] type to read its module from.");
        }

        return segments[1];
    }
}
