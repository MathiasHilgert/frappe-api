using Frappe.Http;
using Frappe.Identity.Application.Owners.LogInOwner;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Http.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace Frappe.Identity.Http.Owners;

/// <summary>
/// Logging an owner in (S7): <c>POST /sessions</c> with an address and a password opens a session. It is anonymous, never cached and has no
/// <c>Location</c>: a session is addressed as <c>current</c>, never by id.
/// </summary>
internal static class LogInEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapLogInEndpoints() =>
            routes.MapPost("/sessions", LogInAsync)
                .WithName("LogIn").WithTags(IdentityHttp.Tag).NoStore().WithoutLocation()
                .RequireRateLimiting(IdentityHttp.LogInRateLimitPolicy)
                .WithSummary("Log in")
                .WithDescription(
                    "Opens a session for the owner who has this address and password. It answers 201 with the session in the body (send its token "
                    + "as Authorization: Bearer) and, for the web console, in the session cookie. A wrong password, an address nobody has and "
                    + "an address that cannot log in answer the same 401 identity.credentials.invalid, never faster than half a second. An owner "
                    + "whose address is not verified yet and who gives the right password is sent a new code and answered 403 "
                    + "identity.owner.verification_pending, whose parameters.ownerId is the id to verify with. An address that tries too often "
                    + $"is throttled with 429 http.rate_limited and Retry-After ({LogInLimits.Attempt.Limit} attempts per address from one "
                    + $"network in {LogInLimits.Attempt.Window.TotalMinutes:0} minutes, and {LogInLimits.AttemptByEmail.Limit} per address from "
                    + $"any network in {LogInLimits.AttemptByEmail.Window.TotalHours:0} hour, whatever their outcome), and so is a client "
                    + "address that logs in "
                    + "too often; there is no lockout beyond the window. 503 identity.attempts.unavailable while attempts cannot be counted.")
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Created<OpenedSessionResponse>, ErrorsProblemHttpResult>> LogInAsync(
        [FromBody] LogInRequest? body,
        HttpContext httpContext,
        ICommandHandler<LogInOwner, LogInOutcome> handler,
        CancellationToken cancellationToken)
    {
        var command = new LogInOwner(
            body?.Email, body?.Password, httpContext.Request.Headers[HeaderNames.UserAgent].ToString(), httpContext.Connection.RemoteIpAddress);
        var result = await handler.HandleAsync(command, cancellationToken);
        if (result is { IsSuccess: true, Value.PendingOwnerId: { } ownerId })
        {
            // Exception to SEC-06: this answer says an owner exists, but only to whoever gave that owner's right password, who is the owner and
            // needs the id to verify with. Every answer that does not hold the right password stays the one 401.
            Result<OpenedSession> pending = new ForbiddenError(IdentityErrorCodes.OwnerVerificationPending, ("ownerId", ownerId.ToString()));
            return pending.ToTypedHttpResult(opened => SessionAnswers.Created(httpContext, opened));
        }

        return result.Map(outcome => outcome.Session!).ToTypedHttpResult(opened => SessionAnswers.Created(httpContext, opened));
    }
}
