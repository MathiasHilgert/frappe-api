using Frappe.Identity.Application.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Frappe.Identity.Http.Sessions;

/// <summary>How a route that opened a session answers: the session in the body and, for the web console, in its cookie.</summary>
internal static class SessionAnswers
{
    /// <summary>
    /// The 201 with <paramref name="opened"/> in the body (no <c>Location</c>: a session is not addressed by id) and in the cookie.
    /// </summary>
    public static Created<OpenedSessionResponse> Created(HttpContext httpContext, OpenedSession opened)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(opened);
        var absolute = opened.AbsoluteExpiresAt.ToDateTimeOffset();
        IdentitySessionCookie.Append(httpContext.Response, opened.Token.Value, absolute);
        return TypedResults.Created(
            (string?)null, new OpenedSessionResponse(opened.Id, opened.Token.Value, opened.IdleExpiresAt.ToDateTimeOffset(), absolute));
    }
}
