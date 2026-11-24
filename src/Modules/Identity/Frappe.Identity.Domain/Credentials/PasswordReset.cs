using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Credentials;

/// <summary>A principal set a new password with a reset code (S11).</summary>
/// <param name="PrincipalId">The principal whose password was reset.</param>
/// <param name="At">When the password was reset.</param>
public sealed record PasswordReset(Guid PrincipalId, Instant At) : IDomainEvent;
