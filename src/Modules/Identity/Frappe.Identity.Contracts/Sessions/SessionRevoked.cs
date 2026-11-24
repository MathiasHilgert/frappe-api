using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Sessions;

/// <summary>A session was revoked, by its holder from another session or because a credential changed.</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="Reason">
/// Why: <c>revoked_by_holder</c>, <c>others_revoked</c>, <c>password_changed</c>, <c>password_reset</c> or <c>email_changed</c>.
/// </param>
/// <param name="OccurredAt">When the session was revoked.</param>
[IntegrationEvent("frappe.identity.session_revoked.v1")]
public sealed record SessionRevoked(Guid PrincipalId, Guid SessionId, string Reason, DateTimeOffset OccurredAt) : IIntegrationEvent;
