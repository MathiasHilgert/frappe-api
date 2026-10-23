using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.PasswordResets;

/// <summary>
/// The reset id to confirm against; otherwise the broken input rules, or how long to wait when rate limited.
/// </summary>
public sealed record PasswordResetRequested(Guid ResetId, Result Validation, TimeSpan RetryAfter)
{
    public static PasswordResetRequested Accepted(Guid resetId) => new(resetId, Result.Success(), TimeSpan.Zero);
    public static PasswordResetRequested Invalid(Result validation) => new(Guid.Empty, validation, TimeSpan.Zero);
    public static PasswordResetRequested Limited(TimeSpan retryAfter) => new(Guid.Empty, Result.Success(), retryAfter);
}
