namespace Frappe.Identity.Contracts.Owners;

/// <summary>An owner asked to reset their password; the code goes to their current email. Carries the plaintext code (identity stores only its hash), single-use and valid for 15 minutes.</summary>
public sealed record OwnerPasswordResetCodeIssued(Guid PrincipalId, string Email, string Code);
