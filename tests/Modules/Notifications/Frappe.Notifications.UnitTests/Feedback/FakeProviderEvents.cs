using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.ProviderEvents;

namespace Frappe.Notifications.UnitTests.Feedback;

/// <summary>An in-memory record of provider events: what was staged, and what was handled before.</summary>
internal sealed class FakeProviderEvents : IProviderEvents
{
    public List<ProviderEvent> Staged { get; } = [];

    public HashSet<string> Known { get; } = [];

    public Task<bool> ExistsAsync(DeliveryProvider provider, string eventId, CancellationToken cancellationToken) =>
        Task.FromResult(Known.Contains(eventId));

    public void Stage(ProviderEvent providerEvent) => Staged.Add(providerEvent);
}
