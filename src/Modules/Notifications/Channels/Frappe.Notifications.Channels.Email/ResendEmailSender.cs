using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Domain.Deliveries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// Takes a rendered email to Resend (<c>POST /emails</c>) with the delivery's idempotency key (S14). The client already retries,
/// rate-limits and times out (see <see cref="EmailSenderRegistration"/>); this class only tells the answers apart: a 2xx is sent; a 429,
/// 5xx or 408, and the 409 <c>concurrent_idempotent_requests</c>, throw <see cref="TransientEmailFailureException"/> so the message is
/// redelivered; the 409 <c>invalid_idempotent_request</c> throws <see cref="EmailIdempotencyConflictException"/> and is alerted on; a 401 or 403
/// (the key)
/// throws <see cref="EmailProviderConfigurationException"/>, a configuration fault and never a verdict on the recipient; a network fault,
/// timeout, open circuit or refused rate-limiter lease is counted transient and thrown as <see cref="TransientEmailFailureException"/>; any other
/// 4xx is a permanent <see cref="DeliveryFailureReason.ProviderRejected"/>. It logs the status and Resend's error name, never a body, an
/// address or the key (S13, CFG-10).
/// </summary>
public sealed partial class ResendEmailSender(
    HttpClient client,
    IOptions<EmailOptions> options,
    EmailProviderMetrics metrics,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    /// <summary>The name this provider is counted under.</summary>
    private const string Provider = "resend";

    private const string ConcurrentConflict = "concurrent_idempotent_requests";

    private const string InvalidConflict = "invalid_idempotent_request";

    /// <summary>The inline logos the HTML references by <c>cid:</c>, encoded once: the same bytes on every attempt.</summary>
    private static readonly ResendAttachment[] Logos =
    [
        .. EmailInlineImage.Logos.Select(image => new ResendAttachment(
            image.FileName, Convert.ToBase64String(image.Bytes), image.ContentId, image.ContentType)),
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<ChannelOutcome> SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var body = new ResendRequest(
            options.Value.From,
            [email.To.Value],
            email.Subject,
            email.Html,
            email.Text,
            email.Headers.Count == 0 ? null : email.Headers,
            Logos);
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails") { Content = JsonContent.Create(body, options: Json) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", email.IdempotencyKey);

        using var response = await SendOrThrowTransientAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await SentAsync(response, cancellationToken);
        }

        var status = (int)response.StatusCode;
        var name = await ErrorNameAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict && name == InvalidConflict)
        {
            metrics.IdempotencyConflict(Provider);
            LogIdempotencyConflict(logger, status, InvalidConflict);
            throw new EmailIdempotencyConflictException($"Resend refused the idempotency key with {status} {InvalidConflict}.");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            metrics.Unauthorized(Provider);
            LogUnauthorized(logger, status, name ?? "unknown");
            throw new EmailProviderConfigurationException($"Resend refused the API key with {status} {name}.");
        }

        if (IsTransient(response.StatusCode, name))
        {
            metrics.Transient(Provider);
            LogTransient(logger, status, name ?? "unknown");
            throw new TransientEmailFailureException($"Resend answered {status} {name}: the send will be retried.");
        }

        metrics.Rejected(Provider);
        LogRejected(logger, status, name ?? "unknown");
        return new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected);
    }

    /// <summary>
    /// Calls the provider; a network fault, a timeout, an open circuit or a refused rate-limiter lease is counted as transient and rethrown as
    /// <see cref="TransientEmailFailureException"/>, so the message is redelivered. A cancellation the caller asked for passes through.
    /// </summary>
    private async Task<HttpResponseMessage> SendOrThrowTransientAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or TaskCanceledException or TimeoutRejectedException or BrokenCircuitException
                or RateLimiterRejectedException)
        {
            metrics.Transient(Provider);
            LogUnreachable(logger, exception.GetType().Name);
            throw new TransientEmailFailureException(
                $"Resend could not be reached ({exception.GetType().Name}): the send will be retried.", exception);
        }
    }

    private static bool IsTransient(HttpStatusCode status, string? name) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
        || (int)status >= 500
        || (status == HttpStatusCode.Conflict && name == ConcurrentConflict);

    private async Task<ChannelOutcome> SentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var id = await ReadAsync(response, root => root.TryGetProperty("id", out var value) ? value.GetString() : null, cancellationToken);
        if (string.IsNullOrWhiteSpace(id))
        {
            metrics.Transient(Provider);
            LogTransient(logger, (int)response.StatusCode, "no_id");
            throw new TransientEmailFailureException("Resend answered 2xx without a message id: the send will be retried with the same key.");
        }

        metrics.Sent(Provider);
        return new ChannelOutcome.Sent(DeliveryProvider.Resend, id, null);
    }

    private static Task<string?> ErrorNameAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        ReadAsync(response, root => root.TryGetProperty("name", out var value) ? value.GetString() : null, cancellationToken);

    /// <summary>Reads one string out of a JSON answer; an answer that is not JSON of that shape yields <see langword="null"/>.</summary>
    private static async Task<string?> ReadAsync(HttpResponseMessage response, Func<JsonElement, string?> read, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object ? read(document.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resend answered {Status} {Error}; the send will be retried")]
    private static partial void LogTransient(ILogger logger, int status, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Resend answered {Status} {Error}: the API key is missing, wrong or not allowed to send")]
    private static partial void LogUnauthorized(ILogger logger, int status, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resend could not be reached ({Failure}); the send will be retried")]
    private static partial void LogUnreachable(ILogger logger, string failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resend refused the email for good with {Status} {Error}")]
    private static partial void LogRejected(ILogger logger, int status, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Resend answered {Status} {Error}: one idempotency key reached it with two different bodies")]
    private static partial void LogIdempotencyConflict(ILogger logger, int status, string error);

    /// <summary>The body Resend's <c>POST /emails</c> takes: sender, recipient, both formats, extra headers and inline images.</summary>
    private sealed record ResendRequest(
        string From,
        string[] To,
        string Subject,
        string Html,
        string Text,
        IReadOnlyDictionary<string, string>? Headers,
        ResendAttachment[] Attachments);

    /// <summary>An inline image: Resend shows it where the HTML says <c>cid:</c> followed by <paramref name="ContentId"/>.</summary>
    private sealed record ResendAttachment(
        string Filename,
        string Content,
        [property: JsonPropertyName("content_id")] string ContentId,
        [property: JsonPropertyName("content_type")] string ContentType);
}
