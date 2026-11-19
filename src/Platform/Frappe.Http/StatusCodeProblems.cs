using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Frappe.Http;

/// <summary>
/// Every <c>4xx</c> answered without a body: the framework's own (a request it cannot read, 400; an unknown route, 404;
/// a method the route does not answer, 405 with its <c>Allow</c>; a body too large, 413; a body in a media type the
/// endpoint does not read, 415) and a bare client-error status an endpoint returns. They become the same Problem
/// Details as every other error (<see cref="PlatformProblem.ForStatus"/>), so no client error is ever an empty body.
/// Responses that already have a body (a use case's 404, a 429) never reach here: the middleware only runs on an empty
/// response. A bodiless <c>5xx</c> is left as it is: it would otherwise lose its status to a 500. A <c>415</c> to a
/// <c>PATCH</c> also names the accepted patch format in <c>Accept-Patch</c> (RFC 5789 §2.2), whether routing refused
/// the media type or the endpoint did: the endpoint's own when it is known, otherwise JSON Merge Patch, Frappé's format
/// for every patch (<see cref="PlatformProblem.AcceptedMediaTypes"/>).
/// </summary>
internal static class StatusCodeProblems
{
    public static Task WriteAsync(StatusCodeContext context)
    {
        var httpContext = context.HttpContext;
        var status = httpContext.Response.StatusCode;
        if (status == StatusCodes.Status415UnsupportedMediaType && HttpMethods.IsPatch(httpContext.Request.Method))
        {
            httpContext.Response.Headers[MergePatch.AcceptPatchHeader] = string.Join(", ", PlatformProblem.AcceptedMediaTypes(httpContext));
        }

        return status is >= 400 and < 500
            ? PlatformProblem.WriteAsync(httpContext, PlatformProblem.ForStatus(httpContext, status))
            : Task.CompletedTask;
    }
}
