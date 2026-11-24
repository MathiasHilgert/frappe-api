using System.ComponentModel;
using Frappe.Http;
using Frappe.Notifications.Application.Requests.Unsubscribe;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Notifications.Http.Unsubscriptions;

/// <summary>
/// <c>POST /v1/notifications/unsubscriptions?token=...</c>: the one-click unsubscribe of RFC 8058 (S5, S9). The caller is a mail client or a
/// frontend page and has no identity: the signed token of the link is its credential. It answers 204 for a valid token, again for a repeat,
/// and 400 <c>notifications.unsubscribe_token.invalid</c> for one that is missing, tampered or not ours. It never redirects and serves no HTML
/// (the page is the frontend's), reads no form (the <c>List-Unsubscribe=One-Click</c> field of the body is accepted, urlencoded or
/// multipart, and ignored), needs no antiforgery token, and is never cached. The token is a query value by RFC 8058; it is redacted from traces
/// and never logged (SEC-09).
/// </summary>
internal static class UnsubscribeEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapUnsubscribeEndpoints() =>
            routes.MapPost("/notifications/unsubscriptions", HandleAsync)
                .WithName("Unsubscribe").WithTags("Notifications").NoStore().DisableAntiforgery()
                .RequireRateLimiting(NotificationsHttp.UnsubscribeRateLimitPolicy)
                .WithSummary("Unsubscribe from marketing notifications")
                .WithDescription(
                    "The one-click unsubscribe of RFC 8058, for the link of a marketing email. The query token is the signed credential of the "
                    + "link: it names the recipient and the category (only marketing can be turned off) and never expires. It answers 204 when "
                    + "the category is off for the recipient, including when it already was, and 400 notifications.unsubscribe_token.invalid "
                    + "for a token that is missing, tampered or not issued here. The body may be the form of RFC 8058 "
                    + "(List-Unsubscribe=One-Click), urlencoded or multipart, or none; it is not read, and any other type is answered 415. "
                    + "It never redirects.")
                .Accepts<UnsubscribeOneClick>(isOptional: true, "application/x-www-form-urlencoded", "multipart/form-data")
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> HandleAsync(
        [FromQuery, Description("The opaque signed token of the unsubscribe link in the email. A missing one is answered 400.")] string? token,
        ICommandHandler<Unsubscribe, Unit> handler,
        UnsubscribeTokenLimiter limiter,
        CancellationToken cancellationToken)
    {
        if (await limiter.RetryAfterAsync(token, cancellationToken) is { } wait)
        {
            Result<Unit> limited = new RateLimitedError(HttpRegistration.RateLimitedCode, wait);
            return limited.ToTypedHttpResult(_ => TypedResults.NoContent());
        }

        return (await handler.HandleAsync(new Unsubscribe(token), cancellationToken)).ToTypedHttpResult(_ => TypedResults.NoContent());
    }
}
