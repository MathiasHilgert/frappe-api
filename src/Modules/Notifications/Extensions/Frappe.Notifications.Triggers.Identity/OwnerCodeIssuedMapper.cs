using Frappe.Identity.Contracts.Owners;
using Frappe.Notifications.Application.Producers;
using Frappe.Notifications.Messages.Account;

namespace Frappe.Notifications.Triggers.Identity;

/// <summary>
/// Maps <see cref="OwnerCodeIssued"/> to the code mail of its purpose (S18): <c>email_verification</c> to <see cref="AccountActivationCode"/>,
/// <c>password_reset</c> to <see cref="PasswordResetCode"/> and <c>email_change</c> to <see cref="EmailChangeCode"/>. The set of purposes is
/// closed: another purpose fails the message so it is retried and then parked, never silently dropped.
/// </summary>
internal sealed class OwnerCodeIssuedMapper : INotificationProducer<OwnerCodeIssued>
{
    public IReadOnlyList<ProducedNotification> Map(OwnerCodeIssued integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var e = integrationEvent;
        return
        [
            e.Purpose switch
            {
                "email_verification" => ProducedNotification.Of<AccountActivationCode, AccountActivationCodePayload>(
                    new AccountActivationCodePayload(e.Email, e.Language, e.Name, e.Code)),
                "password_reset" => ProducedNotification.Of<PasswordResetCode, PasswordResetCodePayload>(
                    new PasswordResetCodePayload(e.Email, e.Language, e.Name, e.Code)),
                "email_change" => ProducedNotification.Of<EmailChangeCode, EmailChangeCodePayload>(
                    new EmailChangeCodePayload(e.Email, e.Language, e.Name, e.Code)),
                _ => throw new InvalidOperationException($"OwnerCodeIssued has a purpose '{e.Purpose}' no code mail is defined for."),
            },
        ];
    }
}
