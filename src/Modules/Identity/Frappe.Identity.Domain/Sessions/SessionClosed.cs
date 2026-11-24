using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Sessions;

/// <summary>The holder of a session logged out.</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="At">When the session was closed.</param>
public sealed record SessionClosed(Guid PrincipalId, Guid SessionId, Instant At) : IDomainEvent;
