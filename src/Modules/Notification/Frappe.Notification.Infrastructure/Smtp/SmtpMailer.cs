using System.Net.Mail;
using System.Net.Mime;
using Frappe.Notification.Application.Mails;
using Frappe.Notification.Domain;
using Microsoft.Extensions.Options;

namespace Frappe.Notification.Infrastructure.Smtp;

/// <summary>
/// Development mailer: hands every mail to the local Mailpit over plain SMTP, so a developer reads verification codes
/// in its web UI. Registered only in Development, so it never sends real mail.
/// </summary>
internal sealed class SmtpMailer(IOptions<SmtpOptions> options) : IMailer
{
    public async Task SendAsync(Mail mail, string idempotencyKey, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var message = new MailMessage(settings.From, mail.To) { Subject = mail.Subject, Body = mail.Text };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        if (mail.Html is not null)
        {
            var html = AlternateView.CreateAlternateViewFromString(mail.Html, null, MediaTypeNames.Text.Html);
            foreach (var image in mail.InlineImages)
            {
                html.LinkedResources.Add(new LinkedResource(new MemoryStream(image.Content.ToArray()), image.ContentType)
                {
                    ContentId = image.ContentId,
                });
            }
            message.AlternateViews.Add(html);
        }

#pragma warning disable S5332 // Plain SMTP to the local Mailpit container only; it never leaves the developer's machine.
        using var client = new SmtpClient(settings.Host, settings.Port);
#pragma warning restore S5332
        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpException error)
        {
            throw new MailDeliveryFailedException("The local SMTP catcher did not accept the mail.", error);
        }
    }
}
