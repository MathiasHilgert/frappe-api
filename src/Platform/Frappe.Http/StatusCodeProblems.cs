using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Frappe.Http;

/// <summary>
/// Every <c>4xx</c> answered without a body: the framework's own (a request it cannot read, 400; an unknown route, 404;
/// a method the route does not answer, 405 with its <c>Allow</c>; a body too large, 413; a body in a media type the
/// endpoint does not read, 415) and a bare client-error status an endpoint returns. They become the same Problem
/// Details as every other error (<see cref="PlatformProblem.ForStatus"/>), so no client error is ever an empty body.
/// Responses that already have a body (a use case's 404, a 429) never reach here: the middleware only runs on an empty
/// response. A bodiless <c>5xx</c> is left as it is: it would otherwise lose its status to a 500.
/// </summary>
internal static class StatusCodeProblems
{
    public static Task WriteAsync(StatusCodeContext context)
    {
        var httpContext = context.HttpContext;
        return httpContext.Response.StatusCode is >= 400 and < 500
            ? PlatformProblem.WriteAsync(httpContext, PlatformProblem.ForStatus(httpContext, httpContext.Response.StatusCode))
            : Task.CompletedTask;
    }
}
