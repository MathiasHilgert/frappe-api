using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>An owner's earlier address was restored from the notice, and every session was revoked (S15).</summary>
/// <param name="OwnerId">The owner.</param>
/// <param name="At">When the address was restored.</param>
public sealed record OwnerEmailChangeUndone(Guid OwnerId, Instant At) : IDomainEvent;
