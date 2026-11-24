using Frappe.Access.Contracts;
using Frappe.Http;
using Frappe.Identity.Application.Sessions.ListMySessions;
using Frappe.Identity.Application.Sessions.RevokeMyOtherSessions;
using Frappe.Identity.Application.Sessions.RevokeMySession;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// The caller's own sessions (S13, HTTP-08): <c>GET /me/sessions</c> lists the live ones, <c>DELETE /me/sessions/{sessionId}</c> revokes one and
/// <c>DELETE /me/sessions/others</c> revokes all but the one in use. The caller and their current session come from the authenticated request;
/// the id in the URL is allowed because another principal's session answers 404 (SEC-05). Answers are never cached. A cookie request that
/// deletes passes the console origin check before anything runs (SEC-03).
/// </summary>
internal static class MySessionsEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapMySessionsEndpoints()
        {
            routes.MapGet("/me/sessions", ListAsync)
                .WithName("ListMySessions").WithTags(IdentityHttp.Tag).NoStore().RequireSession()
                .WithSummary("List the caller's sessions")
                .WithDescription(
                    "The live sessions of the caller (not revoked, not ended, not expired), newest opened first: the browser and the "
                    + "operating system its user agent names (null when not recognized), the client address as its IPv4 /24 or IPv6 /48 "
                    + "network, when it opened and when it was last used (up to a minute behind), and which one is the session of this "
                    + "request. Paged with limit (1 to "
                    + $"{PageLimits.Max}, {PageLimits.Default} by default) and cursor, the nextCursor of the previous page: 400 "
                    + "platform.page_limit.invalid, platform.page_limit.out_of_range or platform.page_cursor.invalid. 401 "
                    + "identity.session.invalid without a valid session.")
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized);

            routes.MapDelete("/me/sessions/others", RevokeOthersAsync)
                .WithName("RevokeMyOtherSessions").WithTags(IdentityHttp.Tag).NoStore().RequireSession()
                .WithSummary("Revoke every session but the current one")
                .WithDescription(
                    "Revokes every live session of the caller except the one the request uses, whose token keeps working. Having no other "
                    + "session is not an error: it answers 204 all the same. 401 identity.session.invalid without a valid session; 403 "
                    + "http.origin.forbidden for a cookie request from no console origin.")
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            routes.MapDelete("/me/sessions/{sessionId}", RevokeAsync)
                .WithName("RevokeMySession").WithTags(IdentityHttp.Tag).NoStore().RequireSession()
                .WithSummary("Revoke one of the caller's sessions")
                .WithDescription(
                    "Revokes one live session of the caller, the current one included: its token stops working at once. A session of "
                    + "another principal, one that is already over, one that does not exist and an id that is not an id answer the same 404 "
                    + "identity.session.not_found. 401 identity.session.invalid without a valid session; 403 http.origin.forbidden for a "
                    + "cookie request from no console origin.")
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound);
        }
    }

    private const string Scope = "identity.me.sessions";

    private static async Task<Results<Ok<MySessionListResponse>, ErrorsProblemHttpResult>> ListAsync(
        ICaller caller,
        IQueryHandler<ListMySessions, MySessionsPage> handler,
        [DocumentedParameter(Format = ParameterFormat.WholeNumber, Minimum = PageLimits.Min, Maximum = PageLimits.Max)] string? limit,
        [DocumentedParameter] string? cursor,
        CancellationToken cancellationToken)
    {
        // A cursor is bound to the caller's own list, so it cannot carry a position into another principal's.
        var scope = $"{Scope}:{caller.RequiredPrincipalId}";
        var request = PageRequest.Bind<SessionCursorPosition>(limit, cursor, scope, SessionCursorPosition.IsValid);
        if (!request.IsSuccess)
        {
            return Result.Failure<MySessionListResponse>(request.Errors).ToTypedHttpResult(body => TypedResults.Ok(body));
        }

        var after = request.Value.After is { } position ? SessionCursorPosition.ToApplication(position) : null;
        var page = await handler.HandleAsync(new ListMySessions(request.Value.Limit, after), cancellationToken);
        return page.ToTypedHttpResult(view =>
        {
            var next = view.Next is { } end ? SessionCursorPosition.Of(end) : null;
            var answer = Page.Of(
                [.. view.Items.Select(item => new MySessionResponse(
                    item.Id, item.Current, item.Browser, item.Os, item.IpPrefix?.ToString(), item.OpenedAt.ToDateTimeOffset(),
                    item.LastSeenAt.ToDateTimeOffset()))],
                next,
                scope);
            return TypedResults.Ok(new MySessionListResponse(answer.Items, answer.NextCursor));
        });
    }

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> RevokeAsync(
        HttpContext httpContext, string sessionId, ICommandHandler<RevokeMySession, Unit> handler, CancellationToken cancellationToken)
    {
        var principal = SessionPolicy.RequiredPrincipalIdOf(httpContext.User);
        var target = Guid.TryParse(sessionId, out var id) ? id : Guid.Empty;
        return (await handler.HandleAsync(new RevokeMySession(principal, target), cancellationToken))
            .ToTypedHttpResult(_ => TypedResults.NoContent());
    }

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> RevokeOthersAsync(
        ICommandHandler<RevokeMyOtherSessions, Unit> handler, CancellationToken cancellationToken)
    {
        return (await handler.HandleAsync(new RevokeMyOtherSessions(), cancellationToken))
            .ToTypedHttpResult(_ => TypedResults.NoContent());
    }
}
