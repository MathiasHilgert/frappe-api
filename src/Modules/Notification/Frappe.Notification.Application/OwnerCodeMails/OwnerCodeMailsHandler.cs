using Frappe.Identity.Contracts;
using Frappe.Identity.Contracts.PasswordResets;

namespace Frappe.Notification.Application;

/// <summary>
/// Turns identity's code events into mails. Each runs from its own durable queue, after the outbox commits. Codes arrive
/// sealed and are opened only here, in memory, right before the mail is composed.
/// </summary>
public static class OwnerCodeMailsHandler
{
    public static Task Handle(OwnerVerificationCodeIssued issued, IOwnerCodeMails mails, IOwnerCodeSeal seal, IMailer mailer,
        CancellationToken ct) =>
        mailer.SendAsync(mails.Compose(OwnerCodeMailKind.Verification, issued.Email, issued.Name, seal.Open(issued.SealedCode), issued.Locale), ct);

    public static Task Handle(PasswordResetCodeIssued issued, IOwnerCodeMails mails, IOwnerCodeSeal seal, IMailer mailer,
        CancellationToken ct) =>
        mailer.SendAsync(mails.Compose(OwnerCodeMailKind.PasswordReset, issued.Email, issued.Name, seal.Open(issued.SealedCode), issued.Locale), ct);

    public static Task Handle(EmailChangeCodeIssued issued, IOwnerCodeMails mails, IOwnerCodeSeal seal, IMailer mailer,
        CancellationToken ct) =>
        mailer.SendAsync(mails.Compose(OwnerCodeMailKind.EmailChange, issued.Email, issued.Name, seal.Open(issued.SealedCode), issued.Locale), ct);
}
