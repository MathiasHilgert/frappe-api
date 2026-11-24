using Frappe.Http;
using Frappe.Notifications.Application.Requests.SendNotification;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Notifications.Http.Sends;

/// <summary>
/// <c>POST /v1/notifications/sends</c>: asks Notifications to send one notification (S2). It exists only while the host runs as
/// Development (see <see cref="NotificationsHttp"/>): it has no caller identity or permission yet, and the campaigns API that replaces it
/// will. It answers 202 with no <c>Location</c>, since nothing is there to follow: the send happens in the Worker. A retry with the same
/// <c>Idempotency-Key</c> sends once. Never cached.
/// </summary>
internal static class SendNotificationEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapSendNotificationEndpoints() =>
            routes.MapPost("/notifications/sends", HandleAsync)
                .WithName("SendNotification").WithTags("Notifications").NoStore().AcceptsIdempotencyKey()
                .WithSummary("Send a notification")
                .WithDescription(
                    "Asks Notifications to send one notification of a kind with its payload, for development. It answers 202 once the request is "
                    + "accepted: the email is sent afterwards by the Worker, and nothing is stored of its payload. The same Idempotency-Key "
                    + "sends once. Only available when the host runs as Development.")
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Accepted, ErrorsProblemHttpResult>> HandleAsync(
        [FromBody] SendNotificationRequest body,
        [FromHeader(Name = IdempotencyKey.HeaderName)] string? idempotencyKey,
        ICommandHandler<SendNotification, Unit> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var command = new SendNotification(RequestIdentity.Of(httpContext, idempotencyKey), body.Kind ?? string.Empty, body.Payload);
        return (await handler.HandleAsync(command, cancellationToken)).ToTypedHttpResult(_ => TypedResults.Accepted((string?)null));
    }
}
