using System.Net.Http.Headers;
using System.Net.Http.Json;
using Frappe.Notification.Application;
using Frappe.Notification.Domain;
using Microsoft.Extensions.Options;

namespace Frappe.Notification.Infrastructure;

/// <summary>Sends through Resend's HTTP API (POST /emails). A non-success status throws so the queue retries.</summary>
internal sealed class ResendMailer(HttpClient http, IOptions<ResendOptions> options) : IMailer
{
    public async Task SendAsync(Mail mail, CancellationToken ct)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new { from = settings.From, to = new[] { mail.To }, subject = mail.Subject, text = mail.Text }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Resend rejected the mail with status {(int)response.StatusCode}.", null, response.StatusCode);
    }
}
