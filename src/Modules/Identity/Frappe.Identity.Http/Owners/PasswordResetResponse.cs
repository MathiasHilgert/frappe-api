namespace Frappe.Identity.Http.Owners;

/// <summary>The answer of a reset request; it has the same shape whether a code was sent or not.</summary>
/// <param name="Id">The id to confirm the reset with.</param>
public sealed record PasswordResetResponse(Guid Id);
