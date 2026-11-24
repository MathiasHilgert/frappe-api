using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Sessions;

/// <summary>The sweep ended a session that idled out or reached its absolute lifetime.</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="Reason">Which limit ended it.</param>
/// <param name="At">When the sweep ended the session.</param>
public sealed record SessionEnded(Guid PrincipalId, Guid SessionId, SessionEndReason Reason, Instant At) : IDomainEvent;
