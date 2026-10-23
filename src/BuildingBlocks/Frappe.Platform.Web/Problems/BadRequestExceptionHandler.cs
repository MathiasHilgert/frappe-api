using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Web.Problems;

/// <summary>
/// Malformed requests (minimal-API binding failures such as a missing required query parameter) surface as
/// <see cref="BadHttpRequestException"/>. They are the client's fault, so they answer with the exception's own
/// status code as problem details instead of falling through to a 500.
/// </summary>
internal sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
            return false;

        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = badRequest.StatusCode },
        });
    }
}
