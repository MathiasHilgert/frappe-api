using Frappe.Identity.Contracts;
using Frappe.Identity.Contracts.PasswordResets;
using Frappe.Notification.Domain;

namespace Frappe.Notification.Application;

/// <summary>Turns identity's code events into mails. Each runs from its own durable queue, after the outbox commits.</summary>
public static class OwnerCodeMailsHandler
{
    public static Task Handle(OwnerVerificationCodeIssued issued, IMailer mailer, CancellationToken ct) =>
        mailer.SendAsync(OwnerCodeMails.Verification(issued.Email, issued.Code), ct);

    public static Task Handle(PasswordResetCodeIssued issued, IMailer mailer, CancellationToken ct) =>
        mailer.SendAsync(OwnerCodeMails.PasswordReset(issued.Email, issued.Code), ct);

    public static Task Handle(EmailChangeCodeIssued issued, IMailer mailer, CancellationToken ct) =>
        mailer.SendAsync(OwnerCodeMails.EmailChange(issued.Email, issued.Code), ct);
}
