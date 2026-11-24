using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Owners;

/// <summary>An owner restored the earlier address from the notice; every session of the owner was revoked.</summary>
/// <param name="OwnerId">The owner whose address was restored.</param>
/// <param name="OccurredAt">When the address was restored.</param>
[IntegrationEvent("frappe.identity.owner_email_change_undone.v1")]
public sealed record OwnerEmailChangeUndone(Guid OwnerId, DateTimeOffset OccurredAt) : IIntegrationEvent;
