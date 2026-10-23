namespace Frappe.Identity.Contracts;

/// <summary>
/// An owner asked to move their sign-in to <paramref name="Email"/>, the new address, which must receive this code to
/// prove itself. <paramref name="ChangeId"/> is what the confirmation call names. As with the other code messages, this
/// is the only place the plaintext code exists.
/// <c>Locale</c> is the owner's stored BCP-47 locale, so the mail speaks their language; messages written before
/// it existed omit it and are mailed in the platform default.
/// </summary>
public sealed record EmailChangeCodeIssued(
    Guid ChangeId, Guid PrincipalId, string Name, string Email, string Code, DateTimeOffset ExpiresAt, string? Locale = null);
