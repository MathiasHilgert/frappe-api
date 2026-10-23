namespace Frappe.Identity.Contracts;

/// <summary>
/// An owner asked to move their sign-in to <paramref name="Email"/>, the new address, which must receive this code to
/// prove itself. <paramref name="ChangeId"/> is what the confirmation call names. As with the other code messages, this
/// is the only place the plaintext code exists.
/// </summary>
public sealed record EmailChangeCodeIssued(
    Guid ChangeId, Guid PrincipalId, string Name, string Email, string Code, DateTimeOffset ExpiresAt);
