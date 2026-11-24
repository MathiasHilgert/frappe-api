using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Owners;

/// <summary>An owner proved their address with the verification code and became active.</summary>
/// <param name="OwnerId">The owner that was verified.</param>
/// <param name="OccurredAt">When the owner was verified.</param>
[IntegrationEvent("frappe.identity.owner_verified.v1")]
public sealed record OwnerVerified(Guid OwnerId, DateTimeOffset OccurredAt) : IIntegrationEvent;
