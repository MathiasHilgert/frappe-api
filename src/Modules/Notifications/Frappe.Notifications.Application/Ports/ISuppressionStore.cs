using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Suppressions;

namespace Frappe.Notifications.Application.Ports;

/// <summary>The addresses that must not be contacted: read by the gate, added by the webhook (it only stages, UOW-01).</summary>
public interface ISuppressionStore
{
    /// <summary>Whether an address, under any of its hashes, is suppressed over the channel.</summary>
    Task<bool> IsSuppressedAsync(ChannelName channel, IReadOnlyList<AddressHash> hashes, CancellationToken cancellationToken);

    /// <summary>Stages a suppression; the unit of work saves it.</summary>
    void Stage(Suppression suppression);
}
