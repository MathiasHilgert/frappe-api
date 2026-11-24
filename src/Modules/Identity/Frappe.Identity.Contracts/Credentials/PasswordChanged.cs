using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Credentials;

/// <summary>A principal changed their password while signed in; their other sessions were revoked.</summary>
/// <param name="PrincipalId">The principal whose password changed.</param>
/// <param name="OccurredAt">When the password changed.</param>
[IntegrationEvent("frappe.identity.password_changed.v1")]
public sealed record PasswordChanged(Guid PrincipalId, DateTimeOffset OccurredAt) : IIntegrationEvent;
