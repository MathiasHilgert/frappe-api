namespace Frappe.Identity.Contracts.Owners;

/// <summary>An owner asked to change email; the code goes to the new address to prove they own it. Carries the plaintext code (identity stores only its hash), single-use and valid for 15 minutes.</summary>
public sealed record OwnerEmailChangeCodeIssued(Guid PrincipalId, string NewEmail, string Code);
