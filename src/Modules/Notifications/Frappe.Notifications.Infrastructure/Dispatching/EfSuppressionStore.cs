using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Dispatching;

/// <summary>Reads suppressions by channel and keyed hash: one probe per active key.</summary>
internal sealed class EfSuppressionStore(NotificationsDbContext db) : ISuppressionStore
{
    public async Task<bool> IsSuppressedAsync(ChannelName channel, IReadOnlyList<AddressHash> hashes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(hashes);
        var name = channel.Value;
        foreach (var hash in hashes)
        {
            var bytes = hash.Value;
            var keyId = hash.KeyId;
            if (await db.Suppressions.AsNoTracking()
                    .AnyAsync(s => s.Channel == name && s.HashKeyId == keyId && s.AddressHash == bytes, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
