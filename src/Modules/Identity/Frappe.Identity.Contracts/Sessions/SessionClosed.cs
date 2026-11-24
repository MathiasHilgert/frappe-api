using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Sessions;

/// <summary>The holder of a session logged out.</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="OccurredAt">When the session was closed.</param>
[IntegrationEvent("frappe.identity.session_closed.v1")]
public sealed record SessionClosed(Guid PrincipalId, Guid SessionId, DateTimeOffset OccurredAt) : IIntegrationEvent;
