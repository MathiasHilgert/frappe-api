using System.Text.Json;
using Frappe.Kernel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Http.Problems;

/// <summary>
/// Turns what escapes an endpoint into the house problem: a bad request keeps its status and anything else is a 500
/// without internals. A client disconnect never reaches it: the exception handler middleware answers it with 499, no
/// body and a Debug log, so it is not counted as a server error.
/// </summary>
internal sealed class ExceptionHandler(
    IProblemDetailsService problems,
    ILogger<ExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        (int status, string type, Error error) = exception switch
        {
            BadHttpRequestException bad => (
                bad.StatusCode,
                ProblemTypes.MalformedRequest,
                WithPointer(PlatformErrors.Malformed, bad.InnerException as JsonException)
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                ProblemTypes.Internal,
                PlatformErrors.Failed
            ),
        };
        if (status >= StatusCodes.Status500InternalServerError)
        {
            Log.Unhandled(logger, exception, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = ProblemMapper.Create(httpContext, status, type, [error]),
                Exception = exception,
            }
        );
    }

    // System.Text.Json paths look like $.items[3].name; a JSON Pointer is /items/3/name.
    private static Error WithPointer(Error error, JsonException? json)
    {
        if (json?.Path is not { Length: > 1 } path || !path.StartsWith('$'))
        {
            return error;
        }

        string pointer = path[1..]
            .Replace("['", "/", StringComparison.Ordinal)
            .Replace("']", "", StringComparison.Ordinal)
            .Replace('[', '/')
            .Replace("]", "", StringComparison.Ordinal)
            .Replace('.', '/');
        ErrorOr<Success> failed = error;
        return failed.WithPointer(pointer).FirstError;
    }
}
