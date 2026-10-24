using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Frappe.Notification.Application.Mails;
using Frappe.Notification.Domain;
using Microsoft.Extensions.Options;

namespace Frappe.Notification.Infrastructure.Resend;

/// <summary>
/// Sends through Resend's HTTP API (POST /emails). A 4xx other than 429 is a <see cref="MailRejectedException"/>;
/// 429, 5xx, timeouts and network failures are a <see cref="MailDeliveryFailedException"/> the queue retries.
/// </summary>
internal sealed class ResendMailer(HttpClient http, IOptions<ResendOptions> options) : IMailer
{
    public async Task SendAsync(Mail mail, string idempotencyKey, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(ToResendEmail(mail, settings.From), ResendJsonContext.Default.ResendEmail),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        // Resend drops a repeat of the same key for 24 hours, so a retry after a timeout cannot send the code twice.
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                                      && !cancellationToken.IsCancellationRequested)
        {
            throw new MailDeliveryFailedException("Resend could not be reached.", error);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return;
            var status = response.StatusCode;
            var code = (int)status;
            if (code is >= 400 and < 500 && status != HttpStatusCode.TooManyRequests)
                throw new MailRejectedException(status);
            throw new MailDeliveryFailedException($"Resend failed the mail with status {code}.", null, status);
        }
    }

    private static ResendEmail ToResendEmail(Mail mail, string from) => new(
        from,
        [mail.To],
        mail.Subject,
        mail.Text,
        mail.Html,
        mail.InlineImages.Count == 0
            ? null
            : [.. mail.InlineImages.Select(i =>
                new ResendAttachment(i.FileName, Convert.ToBase64String(i.Content.Span), i.ContentType, i.ContentId))]);
}
