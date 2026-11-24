using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>A pending owner never verified their address within the retention period and was deleted with their data (S16).</summary>
/// <param name="OwnerId">The owner that was deleted.</param>
/// <param name="At">When the owner was purged.</param>
public sealed record OwnerPendingPurged(Guid OwnerId, Instant At) : IDomainEvent;
