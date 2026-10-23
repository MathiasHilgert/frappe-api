namespace Frappe.Identity.Contracts;

/// <summary>
/// A verification code was issued to a pending owner (on registration or on resend) and must be emailed. It is the
/// only place the plaintext code exists: the database keeps its hash, and the outbox row is gone once delivered.
/// </summary>
public sealed record OwnerVerificationCodeIssued(Guid PrincipalId, string Name, string Email, string Code, DateTimeOffset ExpiresAt);
