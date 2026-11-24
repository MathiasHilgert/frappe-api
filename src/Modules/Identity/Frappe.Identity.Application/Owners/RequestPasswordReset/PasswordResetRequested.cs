namespace Frappe.Identity.Application.Owners.RequestPasswordReset;

/// <summary>
/// What a reset request answers: the id the confirmation names, which is the id of the code when one was sent and a made-up one when not.
/// </summary>
/// <param name="Id">A UUIDv7 that the confirmation is addressed with.</param>
public sealed record PasswordResetRequested(Guid Id);
