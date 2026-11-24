using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Sessions;

/// <summary>A session was opened for a principal.</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="Audience">The client it was opened for, such as <c>console</c>.</param>
/// <param name="OccurredAt">When the session was opened.</param>
[IntegrationEvent("frappe.identity.session_opened.v1")]
public sealed record SessionOpened(Guid PrincipalId, Guid SessionId, string Audience, DateTimeOffset OccurredAt) : IIntegrationEvent;
