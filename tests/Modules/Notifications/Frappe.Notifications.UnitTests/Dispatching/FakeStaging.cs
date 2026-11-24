using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>A staging port that keeps what was staged and never saves.</summary>
internal sealed class FakeStaging : IDeliveryStaging
{
    public List<Delivery> Staged { get; } = [];

    /// <summary>What the provider reported before the delivery existed, by message id.</summary>
    public Dictionary<string, List<ParkedFeedback>> Parked { get; } = [];

    public bool AlreadyRecorded { get; set; }

    public Task<bool> ExistsAsync(DeliveryTarget target, CancellationToken cancellationToken) => Task.FromResult(AlreadyRecorded);

    public Task<IReadOnlyList<ParkedFeedback>> ParkedForAsync(
        DeliveryProvider provider, string providerMessageId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ParkedFeedback>>(Parked.TryGetValue(providerMessageId, out var parked) ? [.. parked] : []);

    public void Stage(Delivery delivery) => Staged.Add(delivery);
}
