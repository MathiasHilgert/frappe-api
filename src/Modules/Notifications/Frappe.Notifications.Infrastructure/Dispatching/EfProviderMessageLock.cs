using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Dispatching;

/// <summary>
/// The webhook's side of the message lock: a session advisory lock on the context's own connection, kept open until the handle is
/// disposed (after the command's commit). It conflicts with the Worker's <c>pg_advisory_xact_lock</c> on the same key
/// (<see cref="ProviderMessageLock"/>). A connection that dies releases it.
/// </summary>
internal sealed class EfProviderMessageLock(NotificationsDbContext db) : IProviderMessageLock
{
    public async Task<IAsyncDisposable> AcquireAsync(DeliveryProvider provider, string providerMessageId, CancellationToken cancellationToken)
    {
        var key = ProviderMessageLock.KeyOf(provider, providerMessageId);
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock(hashtextextended({key}, 0))", cancellationToken);
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }

        return new Held(db, key);
    }

    /// <summary>The lock while it is held: disposing releases it and the connection.</summary>
    private sealed class Held(NotificationsDbContext db, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock(hashtextextended({key}, 0))", CancellationToken.None);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
