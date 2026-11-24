using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Dispatching;

/// <summary>Reads suppressions by channel and keyed hash: one query for every hash of every active key.</summary>
internal sealed class EfSuppressionStore(NotificationsDbContext db) : ISuppressionStore
{
    public async Task<bool> IsSuppressedAsync(ChannelName channel, IReadOnlyList<AddressHash> hashes, CancellationToken cancellationToken) =>
        (await SuppressedAmongAsync(channel, hashes, cancellationToken)).Count > 0;

    public async Task<IReadOnlyList<AddressHash>> SuppressedAmongAsync(
        ChannelName channel, IReadOnlyList<AddressHash> hashes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count == 0)
        {
            return [];
        }

        var name = channel.Value;
        var values = hashes.Select(hash => hash.Value).ToArray();
        var keyIds = hashes.Select(hash => hash.KeyId).Distinct().ToArray();

        // address_hash = ANY(values) with the key ids narrows the primary key; the exact (key id, bytes) pairs are matched below.
        var found = await db.Suppressions.AsNoTracking()
            .Where(s => s.Channel == name && keyIds.Contains(s.HashKeyId) && values.Contains(s.AddressHash))
            .Select(s => new { s.HashKeyId, s.AddressHash })
            .ToListAsync(cancellationToken);
        return
        [
            .. hashes.Where(hash => found.Exists(row => row.HashKeyId == hash.KeyId && row.AddressHash.AsSpan().SequenceEqual(hash.Value))),
        ];
    }

    public void Stage(Suppression suppression) => db.Suppressions.Add(suppression);
}
