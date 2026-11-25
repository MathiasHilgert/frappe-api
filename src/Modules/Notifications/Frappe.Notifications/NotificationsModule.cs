using Frappe.Http;
using Frappe.Modules;
using Frappe.Notifications.Http;
using Frappe.Notifications.Infrastructure;
using Frappe.Notifications.Messages.Account;
using Frappe.Notifications.Messages.Marketing;
using Frappe.Notifications.Senders.Email;
using Frappe.Notifications.Triggers.Identity;

namespace Frappe.Notifications;

/// <summary>
/// The door of Notifications: the only thing a host names from the module. <see cref="Api"/> serves it in the Api and <see cref="Worker"/>
/// runs it in the Worker; both start from the services every host shares (<see cref="NotificationsCoreServices"/>), add the kinds, and then
/// only what their host runs. The plug-ins are composed here and nowhere else.
/// </summary>
public static class NotificationsModule
{
    /// <summary>
    /// Notifications in the Api: the shared services, the kinds (<see cref="AccountMessagesModule"/>, <see cref="MarketingMessagesModule"/>), the
    /// email channel's provider webhooks and payload check (<see cref="EmailSenderApiModule"/>), then its HTTP facet
    /// (<see cref="NotificationsHttp"/>): the webhook, unsubscribe and Development-only send routes under <c>/v1</c>.
    /// </summary>
    public static IHttpModule Api { get; } =
        new ComposedHttpModule(
            [new NotificationsCoreServices(), new AccountMessagesModule(), new MarketingMessagesModule(), new EmailSenderApiModule()],
            new NotificationsHttp());

    /// <summary>
    /// Notifications in the Worker: the shared services, the dispatch consumer and the nightly purge (<see cref="NotificationsWorkerServices"/>),
    /// the kinds, the consumers of Identity's facts (<see cref="IdentityTriggersModule"/>), the email channel and its sender
    /// (<see cref="EmailSenderWorkerModule"/>), and the purge of its outbox and inbox messages the nightly purge runs
    /// (<see cref="NotificationsMessagePurgeServices"/>).
    /// </summary>
    public static IModule Worker { get; } =
        new ComposedModule(
            new NotificationsCoreServices(),
            new NotificationsWorkerServices(),
            new AccountMessagesModule(),
            new MarketingMessagesModule(),
            new IdentityTriggersModule(),
            new EmailSenderWorkerModule(),
            new NotificationsMessagePurgeServices());
}
