using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Suppressions;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>An in-memory suppression store: the hashes that are suppressed.</summary>
internal sealed class FakeSuppressions : ISuppressionStore
{
    public List<AddressHash> Suppressed { get; } = [];

    /// <summary>What was staged to be saved.</summary>
    public List<Suppression> Staged { get; } = [];

    public Task<bool> IsSuppressedAsync(ChannelName channel, IReadOnlyList<AddressHash> hashes, CancellationToken cancellationToken) =>
        Task.FromResult(hashes.Any(Suppressed.Contains));

    public void Stage(Suppression suppression)
    {
        Staged.Add(suppression);
        Suppressed.Add(new AddressHash(suppression.AddressHash, suppression.HashKeyId));
    }
}
