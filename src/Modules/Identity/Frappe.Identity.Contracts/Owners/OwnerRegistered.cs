using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Owners;

/// <summary>
/// A registration was accepted: a pending owner exists, or the pending owner of that address was replaced by this registration. It is not
/// raised for an address a verified owner holds (<see cref="OwnerRegistrationAttempted"/> is).
/// </summary>
/// <param name="OwnerId">The new owner's id; also its principal's.</param>
/// <param name="OccurredAt">When the registration was accepted.</param>
[IntegrationEvent("frappe.identity.owner_registered.v1")]
public sealed record OwnerRegistered(Guid OwnerId, DateTimeOffset OccurredAt) : IIntegrationEvent;
