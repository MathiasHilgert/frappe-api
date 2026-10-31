using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;

namespace Frappe.Platform.Http.Problems;

/// <summary>The default request timeout; its bodiless 504 becomes the house problem like any other status.</summary>
public static class RequestTimeouts
{
    /// <summary>How long a request may run before it is cancelled and answered with 504.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    internal static void Configure(RequestTimeoutOptions options) =>
        options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = Default,
            TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
        };
}
