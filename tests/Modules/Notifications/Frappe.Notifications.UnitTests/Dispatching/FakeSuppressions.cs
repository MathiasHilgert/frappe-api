using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>An in-memory suppression store: the hashes that are suppressed.</summary>
internal sealed class FakeSuppressions : ISuppressionStore
{
    public List<AddressHash> Suppressed { get; } = [];

    public Task<bool> IsSuppressedAsync(ChannelName channel, IReadOnlyList<AddressHash> hashes, CancellationToken cancellationToken) =>
        Task.FromResult(hashes.Any(Suppressed.Contains));
}
