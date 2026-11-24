using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>
/// An owner confirmed a new address and it replaced the old one (S15). It carries what the notice to the old address needs, among it the plain
/// text of the undo token (a secret): the addresses, the name and the token are hidden when it prints (DOM-06).
/// </summary>
/// <param name="OwnerId">The owner.</param>
/// <param name="OldEmail">The address that was replaced, to mail the notice to.</param>
/// <param name="UndoToken">The single-use token that restores <paramref name="OldEmail"/>.</param>
/// <param name="Name">The name the notice greets with.</param>
/// <param name="Language">The language of the notice.</param>
/// <param name="At">When the address changed.</param>
public sealed record OwnerEmailChanged(Guid OwnerId, string OldEmail, string UndoToken, string Name, string Language, Instant At) : IDomainEvent
{
    public override string ToString() =>
        $"OwnerEmailChanged {{ OwnerId = {OwnerId}, OldEmail = ***, UndoToken = ***, Name = ***, Language = {Language}, At = {At} }}";
}
