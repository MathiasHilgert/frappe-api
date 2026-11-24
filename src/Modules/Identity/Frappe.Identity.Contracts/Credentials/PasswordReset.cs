using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Credentials;

/// <summary>A principal set a new password with a reset code; every session they had was revoked.</summary>
/// <param name="PrincipalId">The principal whose password was reset.</param>
/// <param name="OccurredAt">When the password was reset.</param>
[IntegrationEvent("frappe.identity.password_reset.v1")]
public sealed record PasswordReset(Guid PrincipalId, DateTimeOffset OccurredAt) : IIntegrationEvent;
