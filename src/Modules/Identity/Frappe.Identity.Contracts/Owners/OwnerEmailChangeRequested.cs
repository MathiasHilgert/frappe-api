using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Owners;

/// <summary>
/// An owner asked to change their address. The old address keeps working until the change is confirmed, and is told through this event. The
/// addresses and the name are personal data: <see cref="ToString"/> prints none of them (EVT-20).
/// </summary>
/// <param name="OwnerId">The owner who asked.</param>
/// <param name="OldEmail">The address in force, to send the notice to.</param>
/// <param name="NewEmailMasked">The requested address with its local part masked, safe to show in the notice.</param>
/// <param name="Language">The BCP 47 language of the notice.</param>
/// <param name="Name">The owner's given name, for the greeting.</param>
/// <param name="OccurredAt">When the change was requested.</param>
[IntegrationEvent("frappe.identity.owner_email_change_requested.v1")]
public sealed record OwnerEmailChangeRequested(
    Guid OwnerId, string OldEmail, string NewEmailMasked, string Language, string Name, DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public override string ToString() =>
        $"OwnerEmailChangeRequested {{ OwnerId = {OwnerId}, OldEmail = ***, NewEmailMasked = ***, Language = {Language}, Name = ***, "
        + $"OccurredAt = {OccurredAt:O} }}";
}
