using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Owners;

/// <summary>
/// An owner confirmed the new address and it replaced the old one; the owner's other sessions were revoked. The old address is told through this
/// event, with a single-use token (valid 7 days) that restores it. The addresses, the name and the token are personal data or a secret:
/// <see cref="ToString"/> prints none of them (EVT-20).
/// </summary>
/// <param name="OwnerId">The owner whose address changed.</param>
/// <param name="OccurredAt">When the address changed.</param>
[IntegrationEvent("frappe.identity.owner_email_changed.v1")]
public sealed record OwnerEmailChanged(Guid OwnerId, DateTimeOffset OccurredAt) : IIntegrationEvent
{
    /// <summary>The address that was replaced, to send the notice to. Absent in events published before the undo existed.</summary>
    public string? OldEmail { get; init; }

    /// <summary>The token that restores <see cref="OldEmail"/>, in plain text. Absent in events published before the undo existed.</summary>
    public string? UndoToken { get; init; }

    /// <summary>The owner's name as the notice greets with. Absent in events published before the undo existed.</summary>
    public string? Name { get; init; }

    /// <summary>The BCP 47 language of the notice. Absent in events published before the undo existed.</summary>
    public string? Language { get; init; }

    public override string ToString() =>
        $"OwnerEmailChanged {{ OwnerId = {OwnerId}, OldEmail = ***, UndoToken = ***, Name = ***, Language = {Language}, "
        + $"OccurredAt = {OccurredAt:O} }}";
}
