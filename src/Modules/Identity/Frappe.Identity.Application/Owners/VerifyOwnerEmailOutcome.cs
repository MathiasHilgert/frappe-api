using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Owners;

/// <summary>The outcome; <see cref="RetryAfter"/> is set only when the attempt limit refused the call.</summary>
public sealed record VerifyOwnerEmailOutcome(Result<OpenedSession> Result, TimeSpan RetryAfter);
