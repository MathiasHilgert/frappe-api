using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>A staging port that keeps what was staged and never saves.</summary>
internal sealed class FakeStaging : IDeliveryStaging
{
    public List<Delivery> Staged { get; } = [];

    public bool AlreadyRecorded { get; set; }

    public Task<bool> ExistsAsync(DeliveryTarget target, CancellationToken cancellationToken) => Task.FromResult(AlreadyRecorded);

    public void Stage(Delivery delivery) => Staged.Add(delivery);
}
