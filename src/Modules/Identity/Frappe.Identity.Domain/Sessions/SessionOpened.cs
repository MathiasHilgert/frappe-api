using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Sessions;

/// <summary>A session was opened for a principal (the module's integration event of the same name is built from it).</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="Audience">The client it was opened for.</param>
/// <param name="At">When the session was opened.</param>
public sealed record SessionOpened(Guid PrincipalId, Guid SessionId, SessionAudience Audience, Instant At) : IDomainEvent;
