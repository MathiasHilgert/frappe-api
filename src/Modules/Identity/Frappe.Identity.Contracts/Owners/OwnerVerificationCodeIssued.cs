namespace Frappe.Identity.Contracts.Owners;

/// <summary>An owner needs the code that verifies their sign-up email. Carries the plaintext code (identity stores only its hash), single-use and valid for 15 minutes.</summary>
public sealed record OwnerVerificationCodeIssued(Guid PrincipalId, string Email, string Code);
