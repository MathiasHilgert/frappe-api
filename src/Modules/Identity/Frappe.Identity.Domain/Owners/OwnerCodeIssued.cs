using Frappe.Aggregates;
using Frappe.Identity.Domain.Codes;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>
/// A one-time code was issued to an owner and must be mailed. It is the only place beyond the integration event that holds the plain text;
/// the address, the name and the code are personal data or a secret and print as nothing (DOM-06).
/// </summary>
/// <param name="OwnerId">The owner.</param>
/// <param name="Purpose">What the code proves.</param>
/// <param name="Email">The address to mail.</param>
/// <param name="Name">The name the mail greets with.</param>
/// <param name="Language">The language of the mail.</param>
/// <param name="Code">The six digits.</param>
/// <param name="At">When the code was issued.</param>
public sealed record OwnerCodeIssued(Guid OwnerId, CodePurpose Purpose, string Email, string Name, string Language, string Code, Instant At)
    : IDomainEvent
{
    public override string ToString() =>
        $"OwnerCodeIssued {{ OwnerId = {OwnerId}, Purpose = {Purpose}, Email = ***, Name = ***, Language = {Language}, Code = ***, At = {At} }}";
}
