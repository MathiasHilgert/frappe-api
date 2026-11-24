using Frappe.Identity.Contracts.Owners;
using Frappe.Notifications.Application.Producers;
using Frappe.Notifications.Kinds.Account;

namespace Frappe.Notifications.Producers.Identity;

/// <summary>Maps <see cref="OwnerEmailChangeRequested"/> to the <see cref="EmailChangeRequested"/> notice to the old address (S18).</summary>
internal sealed class OwnerEmailChangeRequestedMapper : INotificationProducer<OwnerEmailChangeRequested>
{
    public IReadOnlyList<ProducedNotification> Map(OwnerEmailChangeRequested integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return
        [
            ProducedNotification.Of<EmailChangeRequested, EmailChangeRequestedPayload>(
                new EmailChangeRequestedPayload(
                    integrationEvent.OldEmail, integrationEvent.Language, integrationEvent.Name, integrationEvent.NewEmailMasked)),
        ];
    }
}
