using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Sessions;

/// <summary>The sweep ended a session that idled out or reached its absolute lifetime.</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="Reason">Which limit ended it: <c>idle</c> or <c>absolute</c>.</param>
/// <param name="OccurredAt">When the sweep ended the session.</param>
[IntegrationEvent("frappe.identity.session_ended.v1")]
public sealed record SessionEnded(Guid PrincipalId, Guid SessionId, string Reason, DateTimeOffset OccurredAt) : IIntegrationEvent;
