namespace Frappe.Identity.Contracts.PasswordResets;

/// <summary>
/// A password reset code was issued to an owner. It carries the plain code because the mail that delivers it is its
/// only reader; the code is single-use and expires at <paramref name="ExpiresAt"/>.
/// </summary>
public sealed record PasswordResetCodeIssued(
    Guid ResetId,
    Guid PrincipalId,
    string Email,
    string Name,
    string Code,
    DateTimeOffset ExpiresAt);
