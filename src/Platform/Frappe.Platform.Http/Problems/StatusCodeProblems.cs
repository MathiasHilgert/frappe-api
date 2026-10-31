using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.Http.Problems;

// The bodiless answers of routing and body reading, written as the house problem; other statuses stay as they are.
internal static class StatusCodeProblems
{
    public static async Task WriteAsync(StatusCodeContext context)
    {
        HttpContext http = context.HttpContext;
        (string type, Error error)? problem = http.Response.StatusCode switch
        {
            StatusCodes.Status404NotFound => (ProblemTypes.NotFound, PlatformErrors.RouteNotFound),
            StatusCodes.Status405MethodNotAllowed => (
                ProblemTypes.MalformedRequest,
                PlatformErrors.MethodNotAllowed
            ),
            StatusCodes.Status413PayloadTooLarge => (
                ProblemTypes.MalformedRequest,
                PlatformErrors.TooLarge
            ),
            StatusCodes.Status415UnsupportedMediaType => (
                ProblemTypes.MalformedRequest,
                PlatformErrors.UnsupportedMediaType
            ),
            _ => null,
        };
        if (problem is not { } found)
        {
            return;
        }

        await http
            .RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = ProblemMapper.Create(
                        http,
                        http.Response.StatusCode,
                        found.type,
                        [found.error]
                    ),
                }
            );
    }
}
