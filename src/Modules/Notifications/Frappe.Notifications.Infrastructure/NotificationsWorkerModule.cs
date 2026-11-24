using Frappe.Messaging.Abstractions;
using Frappe.Modules;
using Frappe.Notifications.Contracts;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Infrastructure;

/// <summary>
/// What only the Worker runs for Notifications: the dispatch consumer (S2, S14). It is declared through the broker-free messaging
/// abstractions, so listing it in a host without a transport records the declaration and runs nothing. The channel senders and the kinds
/// are their plug-ins' own modules.
/// </summary>
public sealed class NotificationsWorkerModule : IModule
{
    /// <summary>The durable name of the dispatch consumer. It is forever: renaming it redelivers the retained stream into its inbox.</summary>
    public const string DispatchConsumer = "notifications-dispatch";

    /// <summary>
    /// The configuration section of the dispatch consumer's delivery, backoff and dead-letter settings. It keeps the transport's defaults:
    /// one message at a time (<c>PrefetchCount</c> 1), which with the sender's rate limiter keeps sends below the provider's 10 requests
    /// per second.
    /// </summary>
    public const string DispatchSection = "Notifications:Dispatch:Consumer";

    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services
            .AddIntegrationEventConsumer<NotificationsDbContext, NotificationRequested>(NotificationsModule.Name, DispatchConsumer, DispatchSection)
            .AddBeforeClaimHandler<NotificationRequested, DispatchNotificationRequested>(DispatchConsumer)
            .AddIntegrationEventHandler<NotificationRequested, ApplyParkedFeedback>(DispatchConsumer);
    }
}
