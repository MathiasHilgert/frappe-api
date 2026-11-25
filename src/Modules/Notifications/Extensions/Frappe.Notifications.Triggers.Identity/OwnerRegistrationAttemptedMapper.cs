using Frappe.Identity.Contracts.Owners;
using Frappe.Notifications.Application.Producers;
using Frappe.Notifications.Messages.Account;

namespace Frappe.Notifications.Triggers.Identity;

/// <summary>Maps <see cref="OwnerRegistrationAttempted"/> to the <see cref="RegistrationAttempted"/> notice to the real owner (S18).</summary>
internal sealed class OwnerRegistrationAttemptedMapper : INotificationProducer<OwnerRegistrationAttempted>
{
    public IReadOnlyList<ProducedNotification> Map(OwnerRegistrationAttempted integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return
        [
            ProducedNotification.Of<RegistrationAttempted, RegistrationAttemptedPayload>(
                new RegistrationAttemptedPayload(integrationEvent.Email, integrationEvent.Language)),
        ];
    }
}
