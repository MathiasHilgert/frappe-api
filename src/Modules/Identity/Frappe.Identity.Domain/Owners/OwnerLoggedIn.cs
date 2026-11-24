using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>
/// An owner proved their password to log in (S7): a session opened, or, for an owner whose address is not verified yet, a new code was asked
/// for. It is not published; it is what counts log-ins once their unit has committed.
/// </summary>
/// <param name="OwnerId">The owner.</param>
/// <param name="VerificationPending">Whether the owner must verify their address before a session opens.</param>
/// <param name="At">When the owner logged in.</param>
public sealed record OwnerLoggedIn(Guid OwnerId, bool VerificationPending, Instant At) : IDomainEvent;
