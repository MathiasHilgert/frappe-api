using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Retention;
using Frappe.Messaging.Retention;

namespace Frappe.Identity;

/// <summary>
/// Connects the Identity retention job to the message purger of its outbox and inbox. The purger belongs to the broker side of messaging,
/// which the Migrator may not load and so the Infrastructure project may not reference; the door, which only the Api and the Worker load,
/// joins them here. The purger exists only where the module declared its message purge (<c>IdentityWorkerServices</c>).
/// </summary>
internal sealed class IdentityMessagePurge(MessagePurger<IdentityDbContext> purger) : IMessagePurge
{
    public async Task PurgeAsync(CancellationToken cancellationToken) => _ = await purger.PurgeAsync(cancellationToken);
}
