using Frappe.Identity.Contracts;
using Frappe.Identity.Contracts.PasswordResets;

namespace Frappe.Notification.Application;

/// <summary>Turns identity's code events into mails. Each runs from its own durable queue, after the outbox commits.</summary>
public static class OwnerCodeMailsHandler
{
    public static Task Handle(OwnerVerificationCodeIssued issued, IOwnerCodeMails mails, IMailer mailer, CancellationToken ct) =>
        mailer.SendAsync(mails.Compose(OwnerCodeMailKind.Verification, issued.Email, issued.Name, issued.Code, issued.Locale), ct);

    public static Task Handle(PasswordResetCodeIssued issued, IOwnerCodeMails mails, IMailer mailer, CancellationToken ct) =>
        mailer.SendAsync(mails.Compose(OwnerCodeMailKind.PasswordReset, issued.Email, issued.Name, issued.Code, issued.Locale), ct);

    public static Task Handle(EmailChangeCodeIssued issued, IOwnerCodeMails mails, IMailer mailer, CancellationToken ct) =>
        mailer.SendAsync(mails.Compose(OwnerCodeMailKind.EmailChange, issued.Email, issued.Name, issued.Code, issued.Locale), ct);
}
