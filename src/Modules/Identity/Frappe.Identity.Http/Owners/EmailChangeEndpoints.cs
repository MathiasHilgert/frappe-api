using Frappe.Http;
using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Owners.ConfirmEmailChange;
using Frappe.Identity.Application.Owners.RequestEmailChange;
using Frappe.Identity.Http.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Identity.Http.Owners;

/// <summary>
/// Changing the owner's address (S15): <c>POST /me/email-changes</c> asks for it (the session must have stepped up) and
/// <c>POST /me/email-changes/{changeId}/confirmation</c> spends the code mailed to the new address. Both take the owner from the session
/// (SEC-01) and are never cached; neither has anything to follow, so none answers a <c>Location</c>.
/// </summary>
internal static class EmailChangeEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapEmailChangeEndpoints()
        {
            routes.MapPost("/me/email-changes", RequestAsync)
                .WithName("RequestEmailChange").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation().RequireOwner().RequireStepUp()
                .WithSummary("Ask to change the email address")
                .WithDescription(
                    $"Mails a {CodeIssuer.Digits}-digit code to the new address and tells the old one about the request. The session must have "
                    + "stepped up (403 identity.step_up.required). The old address keeps working until the change is confirmed. It answers 202 "
                    + "with an id to confirm with, for an address that a verified owner holds as for a free one (that code is not sent and its id "
                    + $"confirms nothing), and never sooner than {EmailChangeLimits.Floor.TotalMilliseconds:0} ms, so neither the answer nor "
                    + $"its time says which. {CodeLimits.Send.Limit} requests an hour are allowed (429 http.rate_limited with Retry-After). 400 "
                    + "identity.email.invalid for an address that is malformed. 401 identity.session.invalid without a valid session; 403 "
                    + "identity.session.forbidden when the session is not an owner's.")
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status429TooManyRequests);

            routes.MapPost("/me/email-changes/{changeId}/confirmation", ConfirmAsync)
                .WithName("ConfirmEmailChange").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation().RequireOwner()
                .WithSummary("Confirm the change of the email address")
                .WithDescription(
                    "Replaces the address with the one the code was mailed to: the new address is proven, every other session of the owner is "
                    + "revoked (the one of the request keeps working) and every other live code of the owner stops working. A pending owner who "
                    + "registered with that address is deleted. 422 identity.code.invalid for a code that is wrong, expired, used or replaced, for "
                    + "an id that is unknown, made up, malformed or not the caller's, and for an address a verified owner holds meanwhile: "
                    + $"a code takes {CodeLimits.Guess.Limit} guesses, an owner {CodeLimits.GuessPerPrincipal.Limit} a day from one network "
                    + $"and {CodeLimits.GuessPerPrincipalAnywhere.Limit} a day from all of them. 503 identity.attempts.unavailable when guesses "
                    + "cannot be counted. 429 http.rate_limited with Retry-After. 401 and 403 as the request.")
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<Results<Accepted<EmailChangeResponse>, ErrorsProblemHttpResult>> RequestAsync(
        [FromBody] RequestEmailChangeRequest? body, HttpContext httpContext, ICommandHandler<RequestEmailChange, EmailChangeRequested> handler,
        TimeProvider time, CancellationToken cancellationToken)
    {
        // The floor starts before the handler and ends after it, and its unit of work committed with it: only the branch that sends a code writes it.
        var started = time.GetTimestamp();
        var principal = SessionPolicy.RequiredPrincipalIdOf(httpContext.User);
        var result = await handler.HandleAsync(new RequestEmailChange(principal, body?.Email), cancellationToken);
        await ResponseFloor.HoldAsync(time, started, EmailChangeLimits.Floor, cancellationToken);
        return result.ToTypedHttpResult(requested => TypedResults.Accepted((string?)null, new EmailChangeResponse(requested.Id)));
    }

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> ConfirmAsync(
        string changeId, [FromBody] ConfirmEmailChangeRequest? body, HttpContext httpContext, ICommandHandler<ConfirmEmailChange, Unit> handler,
        CancellationToken cancellationToken)
    {
        var principal = SessionPolicy.RequiredPrincipalIdOf(httpContext.User);
        var session = SessionClaims.RequiredSessionIdOf(httpContext.User);
        var command = new ConfirmEmailChange(principal, session, changeId, body?.Code, httpContext.Connection.RemoteIpAddress);
        return (await handler.HandleAsync(command, cancellationToken)).ToTypedHttpResult(_ => TypedResults.NoContent());
    }
}
