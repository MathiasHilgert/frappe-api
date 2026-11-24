using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Sessions;

/// <summary>A session proved its holder's password again (the module's integration event of the same name is built from it).</summary>
/// <param name="PrincipalId">The principal the session belongs to.</param>
/// <param name="SessionId">The session.</param>
/// <param name="ValidUntil">Until when the step-up holds.</param>
/// <param name="At">When the step-up was completed.</param>
public sealed record StepUpCompleted(Guid PrincipalId, Guid SessionId, Instant ValidUntil, Instant At) : IDomainEvent;
