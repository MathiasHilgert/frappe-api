using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.Application.Ports;

/// <summary>
/// Where deliveries are recorded. It only stages: the claim of the message the pipeline runs in commits the rows in its own transaction,
/// so a port that saved would split the claim in two (ARCH-13, UOW-01).
/// </summary>
public interface IDeliveryStaging
{
    /// <summary>Whether the delivery of this message, kind, recipient and channel was already recorded (S14).</summary>
    Task<bool> ExistsAsync(DeliveryTarget target, CancellationToken cancellationToken);

    /// <summary>Stages the delivery; the unit of work saves it.</summary>
    void Stage(Delivery delivery);
}
