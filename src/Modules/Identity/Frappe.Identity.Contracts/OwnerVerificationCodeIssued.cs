namespace Frappe.Identity.Contracts;

/// <summary>
/// A verification code was issued to a pending owner (on registration or on resend) and must be emailed. The database
/// keeps the code's hash and this message carries it sealed (<see cref="IOwnerCodeSeal"/>): no row holds it in plaintext.
/// <c>Locale</c> is the owner's stored BCP-47 locale, so the mail speaks their language; messages written before
/// it existed omit it and are mailed in the platform default.
/// </summary>
public sealed record OwnerVerificationCodeIssued(Guid PrincipalId, string Name, string Email, string SealedCode, DateTimeOffset ExpiresAt, string? Locale = null);
