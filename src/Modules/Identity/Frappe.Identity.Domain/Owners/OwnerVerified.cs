using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>An owner proved their address and became active (S6).</summary>
/// <param name="OwnerId">The owner.</param>
/// <param name="At">When the owner was verified.</param>
public sealed record OwnerVerified(Guid OwnerId, Instant At) : IDomainEvent;
