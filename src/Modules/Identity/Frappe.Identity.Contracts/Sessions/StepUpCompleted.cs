using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Sessions;

/// <summary>A session proved its holder's password again and may do sensitive actions until <paramref name="ValidUntil"/>.</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="ValidUntil">Until when the step-up holds; it is never extended by use.</param>
/// <param name="OccurredAt">When the step-up was completed.</param>
[IntegrationEvent("frappe.identity.step_up_completed.v1")]
public sealed record StepUpCompleted(Guid PrincipalId, Guid SessionId, DateTimeOffset ValidUntil, DateTimeOffset OccurredAt) : IIntegrationEvent;
