using Frappe.Http;
using Frappe.Notifications.Application.Feedback;
using Frappe.Notifications.Application.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.Http.Webhooks;

/// <summary>
/// <c>POST /v1/notifications/webhooks/resend</c>: where Resend reports what became of a message (S4). The caller has no identity: the Svix
/// signature of the call is its credential, so a call that is not authentic is answered 400 <c>notifications.webhook_signature.invalid</c>
/// and anything else is 204, including an event the module does not track, a repeat and one that arrives before its delivery (it is parked).
/// It has a rate limit of its own and never logs the body, which holds addresses (S13). Never cached.
/// </summary>
internal static class ResendWebhookEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapResendWebhookEndpoints() =>
            routes.MapPost("/notifications/webhooks/resend", HandleAsync)
                .WithName("ReceiveResendWebhook").WithTags("Notifications").NoStore()
                .RequireRateLimiting(NotificationsHttp.WebhookRateLimitPolicy)
                .WithSummary("Receive a Resend webhook")
                .WithDescription(
                    "Resend reports delivered, bounced, complained, delayed and suppressed messages here. The body is Resend's event as JSON. "
                    + "The call must carry Resend's Svix signature headers: svix-id (the event's unique id), svix-timestamp (the signing time, "
                    + "in Unix seconds) and svix-signature (one or more space-separated v1,<base64> HMAC-SHA256 signatures of "
                    + "'{svix-id}.{svix-timestamp}.{body}' under the webhook secret), with a timestamp within "
                    + $"{WebhookSignature.Tolerance.TotalMinutes:0} minutes of now; otherwise it is answered 400 "
                    + "notifications.webhook_signature.invalid. Every authentic call is answered 204, whether or not the module tracks the "
                    + "event, and a repeated event changes nothing. 409 means a concurrent duplicate of the same event was being written: "
                    + "Resend redelivers it and the retry is answered 204. 503 means the database is unavailable, and Resend retries too. "
                    + "A body over 64 KiB is answered 413.")
                .Accepts<object>("application/json")
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>The largest body read: Resend's events are a few kilobytes.</summary>
    private const long MaxBodyBytes = 64 * 1024;

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> HandleAsync(
        [FromHeader(Name = "svix-id")] string? id,
        [FromHeader(Name = "svix-timestamp")] string? timestamp,
        [FromHeader(Name = "svix-signature")] string? signature,
        [FromKeyedServices(ProviderFeedbackParsers.Resend)] IProviderFeedbackParser parser,
        ProviderFeedbackIntake intake,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxBodyBytes;
        }

        using var body = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(body, cancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add(headers, "svix-id", id);
        Add(headers, "svix-timestamp", timestamp);
        Add(headers, "svix-signature", signature);

        var parsed = parser.Parse(new ProviderWebhook(headers, body.ToArray()));
        if (!parsed.IsSuccess)
        {
            return parsed.ToTypedHttpResult(_ => TypedResults.NoContent());
        }

        return parsed.Value is { } feedback
            ? (await intake.RecordAsync(feedback, cancellationToken)).ToTypedHttpResult(_ => TypedResults.NoContent())
            : TypedResults.NoContent();
    }

    private static void Add(Dictionary<string, string> headers, string name, string? value)
    {
        if (value is not null)
        {
            headers[name] = value;
        }
    }
}
