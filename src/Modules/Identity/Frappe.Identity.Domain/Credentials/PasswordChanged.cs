using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Credentials;

/// <summary>A principal changed their password while signed in (S12).</summary>
/// <param name="PrincipalId">The principal whose password changed.</param>
/// <param name="At">When the password changed.</param>
public sealed record PasswordChanged(Guid PrincipalId, Instant At) : IDomainEvent;
