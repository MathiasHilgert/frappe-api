using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>A registration was accepted: a new pending owner, or a pending owner replaced by a newer registration (S3).</summary>
/// <param name="OwnerId">The owner; also its principal's id.</param>
/// <param name="Replaced">Whether it took over an earlier pending registration of the same address.</param>
/// <param name="At">When the registration was accepted.</param>
public sealed record OwnerRegistered(Guid OwnerId, bool Replaced, Instant At) : IDomainEvent;
