using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Owners;

/// <summary>A pending owner never verified their address within the retention period and was deleted with their data.</summary>
/// <param name="OwnerId">The owner that was deleted.</param>
/// <param name="OccurredAt">When the owner was purged.</param>
[IntegrationEvent("frappe.identity.owner_pending_purged.v1")]
public sealed record OwnerPendingPurged(Guid OwnerId, DateTimeOffset OccurredAt) : IIntegrationEvent;
