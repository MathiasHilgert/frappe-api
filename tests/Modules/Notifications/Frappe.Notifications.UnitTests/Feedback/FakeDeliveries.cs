using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Feedback;

/// <summary>An in-memory set of deliveries found by the provider's message id.</summary>
internal sealed class FakeDeliveries : IDeliveries
{
    public Dictionary<string, Delivery> ByMessage { get; } = [];

    public Task<Delivery?> FindByProviderMessageAsync(DeliveryProvider provider, string providerMessageId, CancellationToken cancellationToken) =>
        Task.FromResult(ByMessage.GetValueOrDefault(providerMessageId));
}
