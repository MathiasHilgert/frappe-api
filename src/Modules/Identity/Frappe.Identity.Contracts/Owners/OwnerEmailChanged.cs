using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Owners;

/// <summary>An owner confirmed the new address and it replaced the old one; the owner's other sessions were revoked.</summary>
/// <param name="OwnerId">The owner whose address changed.</param>
/// <param name="OccurredAt">When the address changed.</param>
[IntegrationEvent("frappe.identity.owner_email_changed.v1")]
public sealed record OwnerEmailChanged(Guid OwnerId, DateTimeOffset OccurredAt) : IIntegrationEvent;
