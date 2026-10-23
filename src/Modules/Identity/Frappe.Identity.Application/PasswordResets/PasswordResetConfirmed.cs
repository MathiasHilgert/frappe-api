using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.PasswordResets;

/// <summary>The outcome; <see cref="RetryAfter"/> is positive only when rate limited.</summary>
public sealed record PasswordResetConfirmed(Result Outcome, TimeSpan RetryAfter);
