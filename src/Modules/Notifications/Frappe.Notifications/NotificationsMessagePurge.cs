using Frappe.Messaging.Retention;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Notifications.Infrastructure.Retention;

namespace Frappe.Notifications;

/// <summary>
/// Connects the Notifications purge job to the message purger of its outbox and inbox. The purger belongs to the broker side of messaging,
/// which the Migrator may not load and so the Infrastructure project may not reference; the door, which only the Api and the Worker load,
/// joins them here. The purger exists only where the module declared its message purge (<c>NotificationsCoreServices</c>), and is resolved
/// when the job runs.
/// </summary>
internal sealed class NotificationsMessagePurge(MessagePurger<NotificationsDbContext> purger) : IMessagePurge
{
    public async Task PurgeAsync(CancellationToken cancellationToken) => _ = await purger.PurgeAsync(cancellationToken);
}
