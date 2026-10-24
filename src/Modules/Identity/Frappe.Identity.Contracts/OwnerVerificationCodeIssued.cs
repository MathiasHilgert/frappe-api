namespace Frappe.Identity.Contracts;

/// <summary>
/// A verification code was issued to a pending owner (on registration or on resend) and must be emailed. It is the
/// only place the plaintext code exists: the database keeps its hash, and the outbox row is gone once delivered.
/// <c>Locale</c> is the owner's stored BCP-47 locale, so the mail speaks their language; messages written before
/// it existed omit it and are mailed in the platform default.
/// </summary>
public sealed record OwnerVerificationCodeIssued(Guid PrincipalId, string Name, string Email, string Code, DateTimeOffset ExpiresAt, string? Locale = null);
