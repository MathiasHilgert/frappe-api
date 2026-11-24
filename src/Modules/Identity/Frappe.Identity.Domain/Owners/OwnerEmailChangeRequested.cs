using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>
/// An owner asked to change their address; the old address must be told. The addresses and the name are personal data and print as nothing
/// (DOM-06).
/// </summary>
/// <param name="OwnerId">The owner.</param>
/// <param name="OldEmail">The address in force, to mail the notice to.</param>
/// <param name="NewEmailMasked">The requested address with its local part hidden.</param>
/// <param name="Name">The name the notice greets with.</param>
/// <param name="Language">The language of the notice.</param>
/// <param name="At">When the change was requested.</param>
public sealed record OwnerEmailChangeRequested(Guid OwnerId, string OldEmail, string NewEmailMasked, string Name, string Language, Instant At)
    : IDomainEvent
{
    public override string ToString() =>
        $"OwnerEmailChangeRequested {{ OwnerId = {OwnerId}, OldEmail = ***, NewEmailMasked = ***, Name = ***, Language = {Language}, At = {At} }}";
}
