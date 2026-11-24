using Frappe.Http;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Sessions.StepUp;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// Stepping up the session a request is using (S14): <c>POST /sessions/current/step-ups</c> asks for the password again and opens a five-minute
/// window. The session is the caller's (SEC-01), never named in the URL; a session is not addressed by id, so the answer has no <c>Location</c>,
/// and it is never cached.
/// </summary>
internal static class StepUpEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapStepUpEndpoints() =>
            routes.MapPost("/sessions/current/step-ups", StepUpAsync)
                .WithName("StepUpSession").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation().RequireSession()
                .WithSummary("Step up the current session")
                .WithDescription(
                    "Asks for the password of the caller again and, when it is right, lets the session do sensitive actions (such as changing "
                    + "the email) until validUntil: five minutes from now, never extended by use; step up again for a new window. 422 "
                    + "identity.password.incorrect when the password is not right (never faster than half a second), and "
                    + $"{PasswordRecheck.Flow.Limit} "
                    + $"tries in {PasswordRecheck.Flow.Window.TotalMinutes:0} minutes are allowed, whatever their outcome, then 429 "
                    + "http.rate_limited with Retry-After (503 identity.attempts.unavailable while tries cannot be counted). 401 "
                    + "identity.session.invalid without a valid session.")
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Created<StepUpResponse>, ErrorsProblemHttpResult>> StepUpAsync(
        [FromBody] StepUpRequest? body, ICommandHandler<StepUp, SteppedUp> handler, CancellationToken cancellationToken)
    {
        var stepped = await handler.HandleAsync(new StepUp(body?.Password), cancellationToken);
        return stepped.ToTypedHttpResult(done => TypedResults.Created((string?)null, new StepUpResponse(done.ValidUntil.ToDateTimeOffset())));
    }
}
