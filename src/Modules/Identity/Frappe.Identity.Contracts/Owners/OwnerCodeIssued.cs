using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Owners;

/// <summary>
/// A one-time code was issued to an owner for one purpose and must be mailed. It is the only event that carries a code, in plain text, because
/// the mail needs it; the outbox row keeps it at most until its retention ends and the code itself dies after 15 minutes. Name, address and code
/// are personal data or a secret: <see cref="ToString"/> prints none of them (EVT-20).
/// </summary>
/// <param name="OwnerId">The owner the code was issued to.</param>
/// <param name="Purpose">What the code proves: <c>email_verification</c>, <c>password_reset</c> or <c>email_change</c>.</param>
/// <param name="Email">The address to mail: the owner's, or the new one for an email change.</param>
/// <param name="Name">The owner's name as the mail says it, given plus family (at most 60 characters, no link), for the greeting.</param>
/// <param name="Language">The BCP 47 language of the mail.</param>
/// <param name="Code">The six digits.</param>
/// <param name="OccurredAt">When the code was issued.</param>
[IntegrationEvent("frappe.identity.owner_code_issued.v1")]
public sealed record OwnerCodeIssued(
    Guid OwnerId, string Purpose, string Email, string Name, string Language, string Code, DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public override string ToString() =>
        $"OwnerCodeIssued {{ OwnerId = {OwnerId}, Purpose = {Purpose}, Email = ***, Name = ***, Language = {Language}, Code = ***, "
        + $"OccurredAt = {OccurredAt:O} }}";
}
