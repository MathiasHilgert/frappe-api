using Frappe.Identity.Contracts.Owners;
using Frappe.Notifications.Application.Producers;
using Frappe.Notifications.Messages.Account;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.Triggers.Identity;

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

#pragma warning disable S1135 // A deliberate, tracked TODO: the page lives in the console, outside this repository.
    // TODO: the console has no undo-email-change page yet. Build it there: it reads the token from the query and calls
    // POST /v1/identity/email-change-undos. Until then the undo button of the email_changed mail leads nowhere.
#pragma warning restore S1135
    private string UndoUrl(string token)
    {
        var console = links.Value.ConsoleUrl
            ?? throw new InvalidOperationException($"{NotificationLinksOptions.Section}:ConsoleUrl is not set.");
        return $"{console.AbsoluteUri.TrimEnd('/')}/{UndoPath}?token={Uri.EscapeDataString(token)}";
    }
}
