using Frappe.Contracts.Abstractions.Messaging;

namespace Frappe.Identity.Contracts;

/// <summary>
/// A verification code was issued to a pending owner (on registration or on resend) and must be emailed. The database
/// keeps the code's hash and this message carries it sealed (<c>IMessageSeal</c>): no row holds it in plaintext.
/// It names the owner by id only: the mailer reads the name, address and locale through
/// <see cref="Queries.IOwnerDirectory"/>, so no personal data sits in the outbox or a queue.
/// </summary>
/// <param name="PrincipalId">The pending owner.</param>
/// <param name="SealedCode">The one-time code, sealed.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
[MessageIdentity("identity.owner_verification_code_issued.v1")]
public sealed record OwnerVerificationCodeIssued(Guid PrincipalId, string SealedCode, DateTimeOffset ExpiresAt);
