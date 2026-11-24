using Frappe.Identity.Contracts.Owners;
using Frappe.Notifications.Application.Producers;
using Frappe.Notifications.Kinds.Account;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Producers.Identity;

/// <summary>Maps <see cref="OwnerEmailChanged"/> to the <see cref="EmailChanged"/> notice, with the undo link, to the old address (S15).</summary>
internal sealed class OwnerEmailChangedMapper(IOptions<NotificationLinksOptions> links) : INotificationProducer<OwnerEmailChanged>
{
    private const string UndoPath = "undo-email-change";

    public IReadOnlyList<ProducedNotification> Map(OwnerEmailChanged integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (string.IsNullOrWhiteSpace(integrationEvent.OldEmail) || string.IsNullOrWhiteSpace(integrationEvent.UndoToken)
            || string.IsNullOrWhiteSpace(integrationEvent.Name))
        {
            // Published before the undo existed: there is nobody to tell and nothing to undo.
            return [];
        }

        return
        [
            ProducedNotification.Of<EmailChanged, EmailChangedPayload>(
                new EmailChangedPayload(
                    integrationEvent.OldEmail, integrationEvent.Language, integrationEvent.Name, UndoUrl(integrationEvent.UndoToken))),
        ];
    }

    private string UndoUrl(string token)
    {
        var console = links.Value.ConsoleUrl
            ?? throw new InvalidOperationException($"{NotificationLinksOptions.Section}:ConsoleUrl is not set.");
        return $"{console.AbsoluteUri.TrimEnd('/')}/{UndoPath}?token={Uri.EscapeDataString(token)}";
    }
}
