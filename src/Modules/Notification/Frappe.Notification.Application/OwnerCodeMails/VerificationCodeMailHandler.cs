using Frappe.Application.Abstractions.Messaging;
using Frappe.Identity.Contracts;
using Frappe.Identity.Contracts.Queries;
using Frappe.Notification.Application.Mails;
using Wolverine;

namespace Frappe.Notification.Application.OwnerCodeMails;

/// <summary>
/// Mails the code of a <see cref="OwnerVerificationCodeIssued"/>, from its own durable queue after the outbox commits. The
/// event carries only the owner's id, so the address, name and locale are read from identity at send time; an owner
/// purged meanwhile gets no mail. The code arrives sealed and is opened only here, in memory, right before the mail is
/// composed. The envelope id is the delivery's idempotency key: the same across retries of this message.
/// </summary>
public static class VerificationCodeMailHandler
{
    public static async Task Handle(
        OwnerVerificationCodeIssued issued, IOwnerDirectory owners, IOwnerCodeMails mails, IMessageSeal seal, IMailer mailer,
        Envelope envelope, CancellationToken cancellationToken)
    {
        if (await owners.FindContactAsync(issued.PrincipalId, cancellationToken) is not { } owner)
            return;

        await mailer.SendAsync(
            mails.Compose(OwnerCodeMailKind.Verification, owner.Email, owner.Name, seal.Open(issued.SealedCode), owner.Locale),
            MailDelivery.KeyFor(envelope.Id),
            cancellationToken);
    }
}
