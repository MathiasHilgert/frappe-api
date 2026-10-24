using Frappe.Application.Abstractions.Messaging;
using Frappe.Identity.Contracts;
using Frappe.Notification.Application.Mails;
using Wolverine;

namespace Frappe.Notification.Application.OwnerCodeMails;

/// <summary>
/// Mails the code of a <see cref="OwnerVerificationCodeIssued"/>, from its own durable queue after the outbox commits. The code arrives
/// sealed and is opened only here, in memory, right before the mail is composed. The envelope id is the delivery's
/// idempotency key: the same across retries of this message, different for every other one.
/// </summary>
public static class VerificationCodeMailHandler
{
    public static Task Handle(
        OwnerVerificationCodeIssued issued, IOwnerCodeMails mails, IMessageSeal seal, IMailer mailer, Envelope envelope,
        CancellationToken cancellationToken) =>
        mailer.SendAsync(
            mails.Compose(OwnerCodeMailKind.Verification, issued.Email, issued.Name, seal.Open(issued.SealedCode), issued.Locale),
            MailDelivery.KeyFor(envelope.Id),
            cancellationToken);
}
