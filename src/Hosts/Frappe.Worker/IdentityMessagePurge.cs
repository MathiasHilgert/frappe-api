using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Retention;
using Frappe.Messaging.Retention;

namespace Frappe.Worker;

/// <summary>
/// Connects the Identity retention job to the message purger of its outbox and inbox. The purger belongs to the broker side of messaging,
/// which the Migrator may not load and so the module's project may not reference; the Worker, which has both, joins them here, as it does for
/// Notifications. The purger exists only where the module declared its message purge (<c>IdentityWorkerModule</c>).
/// </summary>
internal sealed class IdentityMessagePurge(MessagePurger<IdentityDbContext> purger) : IMessagePurge
{
    public async Task PurgeAsync(CancellationToken cancellationToken) => _ = await purger.PurgeAsync(cancellationToken);
}
