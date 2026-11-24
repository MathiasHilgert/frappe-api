using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>An owner confirmed a new address and it replaced the old one (S15).</summary>
/// <param name="OwnerId">The owner.</param>
/// <param name="At">When the address changed.</param>
public sealed record OwnerEmailChanged(Guid OwnerId, Instant At) : IDomainEvent;
