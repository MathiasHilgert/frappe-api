using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Sessions;

/// <summary>A session was revoked for a reason other than its holder logging out of it.</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="Reason">Why it was revoked.</param>
/// <param name="At">When the session was revoked.</param>
public sealed record SessionRevoked(Guid PrincipalId, Guid SessionId, RevocationReason Reason, Instant At) : IDomainEvent;
