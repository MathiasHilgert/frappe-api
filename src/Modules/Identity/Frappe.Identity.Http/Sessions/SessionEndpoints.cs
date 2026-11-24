using Frappe.Http;
using Frappe.Identity.Application.Sessions.GetCurrentSession;
using Frappe.Identity.Application.Sessions.LogOut;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// The session a request is using (S8, S9): <c>GET /sessions/current</c> says when it idles out and ends, and <c>DELETE /sessions/current</c>
/// logs out of it. Both ask for the session (SEC-01, HTTP-08): it comes from the authenticated request, never from the URL. A cookie request
/// that deletes passes the console origin check before anything runs (SEC-03). Answers are never cached.
/// </summary>
internal static class SessionEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapSessionEndpoints()
        {
            routes.MapGet("/sessions/current", GetCurrentAsync)
                .WithName("GetCurrentSession").WithTags(IdentityHttp.Tag).NoStore().RequireSession()
                .WithSummary("Get the current session")
                .WithDescription(
                    "The session the request authenticated with: when it idles out, when it ends whatever its use, and until when its "
                    + "step-up still counts (null when none was made or it has run out). Every authenticated request moves the idle expiry "
                    + "on, never past the absolute one. The session is the bearer token or the console cookie of the request; 401 "
                    + "identity.session.invalid when there is none, or it is unknown, expired, revoked, opened under credentials that "
                    + "changed, or the cookie is sent twice.")
                .ProducesProblem(StatusCodes.Status401Unauthorized);

            routes.MapDelete("/sessions/current", LogOutAsync)
                .WithName("LogOut").WithTags(IdentityHttp.Tag).NoStore().RequireSession()
                .WithSummary("Log out")
                .WithDescription(
                    "Ends the session the request authenticated with: its token stops working at once, and the console cookie is cleared. "
                    + "A request that carries the cookie must come from a console origin (403 http.origin.forbidden otherwise); one that "
                    + "carries a bearer token is not checked. 401 identity.session.invalid when the request has no valid session.")
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<Results<Ok<CurrentSessionResponse>, ErrorsProblemHttpResult>> GetCurrentAsync(
        HttpContext httpContext, IQueryHandler<GetCurrentSession, CurrentSessionView> handler, CancellationToken cancellationToken)
    {
        var session = SessionClaims.RequiredSessionIdOf(httpContext.User);
        return (await handler.HandleAsync(new GetCurrentSession(session), cancellationToken)).ToTypedHttpResult(view =>
            TypedResults.Ok(new CurrentSessionResponse(
                view.Id,
                view.IdleExpiresAt.ToDateTimeOffset(),
                view.AbsoluteExpiresAt.ToDateTimeOffset(),
                view.StepUpValidUntil?.ToDateTimeOffset())));
    }

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> LogOutAsync(
        HttpContext httpContext, ICommandHandler<LogOut, Unit> handler, CancellationToken cancellationToken)
    {
        var principal = SessionPolicy.RequiredPrincipalIdOf(httpContext.User);
        var session = SessionClaims.RequiredSessionIdOf(httpContext.User);
        var loggedOut = await handler.HandleAsync(new LogOut(principal, session), cancellationToken);

        // The cookie goes only once the session is really revoked: a refused commit leaves the holder their token to try again.
        if (loggedOut.IsSuccess)
        {
            IdentitySessionCookie.Clear(httpContext.Response);
        }

        return loggedOut.ToTypedHttpResult(_ => TypedResults.NoContent());
    }
}
