using Frappe.Messaging.Retention;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Notifications.Infrastructure.Retention;

namespace Frappe.Worker;

/// <summary>
/// Connects the Notifications purge job to the message purger of its outbox and inbox. The purger belongs to the broker side of messaging,
/// which the Migrator may not load and so the module's project may not reference; the Worker, which has both, joins them here. The purger
/// itself exists only where the module declared its message purge (<c>NotificationsModule</c>), and is resolved when the job runs.
/// </summary>
internal sealed class NotificationsMessagePurge(MessagePurger<NotificationsDbContext> purger) : IMessagePurge
{
    public async Task PurgeAsync(CancellationToken cancellationToken) => _ = await purger.PurgeAsync(cancellationToken);
}
