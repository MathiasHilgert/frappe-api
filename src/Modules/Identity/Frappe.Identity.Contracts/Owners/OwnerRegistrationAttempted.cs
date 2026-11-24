using Frappe.IntegrationEvents;

namespace Frappe.Identity.Contracts.Owners;

/// <summary>
/// Somebody tried to register an address a verified owner already holds. The caller got the same answer as for a new address; the real owner is
/// told through this event. The address is personal data: <see cref="ToString"/> prints none (EVT-20).
/// </summary>
/// <param name="Email">The address the registration used, as the real owner holds it, to send the notice to.</param>
/// <param name="Language">The BCP 47 language of the real owner's mail.</param>
/// <param name="OccurredAt">When the registration was attempted.</param>
[IntegrationEvent("frappe.identity.owner_registration_attempted.v1")]
public sealed record OwnerRegistrationAttempted(string Email, string Language, DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public override string ToString() =>
        $"OwnerRegistrationAttempted {{ Email = ***, Language = {Language}, OccurredAt = {OccurredAt:O} }}";
}
