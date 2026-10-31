using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.Http.Problems;

/// <summary>The default request timeout and its house 504 answer.</summary>
public static class RequestTimeouts
{
    /// <summary>How long a request may run before it is cancelled and answered with 504.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    internal static void Configure(RequestTimeoutOptions options) =>
        options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = Default,
            TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
            WriteTimeoutResponse = WriteAsync,
        };

    private static async Task WriteAsync(HttpContext context) =>
        await context
            .RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = ProblemMapper.Create(
                        context,
                        StatusCodes.Status504GatewayTimeout,
                        ProblemTypes.Timeout,
                        [PlatformErrors.Timeout]
                    ),
                }
            );
}
