using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Frappe.Notification.Application;
using Frappe.Notification.Domain;
using Microsoft.Extensions.Options;

namespace Frappe.Notification.Infrastructure;

/// <summary>Sends through Resend's HTTP API (POST /emails). A non-success status throws so the queue retries.</summary>
internal sealed class ResendMailer(HttpClient http, IOptions<ResendOptions> options) : IMailer
{
    private static readonly JsonSerializerOptions SkipNulls = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task SendAsync(Mail mail, CancellationToken ct)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new
            {
                from = settings.From,
                to = new[] { mail.To },
                subject = mail.Subject,
                text = mail.Text,
                html = mail.Html,
                // Inline images travel as attachments with a content id, which the HTML references as cid:<id>.
                attachments = mail.InlineImages.Count == 0
                    ? null
                    : mail.InlineImages.Select(i => new
                    {
                        filename = i.FileName,
                        content = Convert.ToBase64String(i.Content.Span),
                        content_type = i.ContentType,
                        content_id = i.ContentId,
                    }).ToArray(),
            }, options: SkipNulls),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Resend rejected the mail with status {(int)response.StatusCode}.", null, response.StatusCode);
    }
}
