using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Ports;

/// <summary>Reads the addresses that must not be contacted.</summary>
public interface ISuppressionStore
{
    /// <summary>Whether an address, under any of its hashes, is suppressed over the channel.</summary>
    Task<bool> IsSuppressedAsync(ChannelName channel, IReadOnlyList<AddressHash> hashes, CancellationToken cancellationToken);
}
