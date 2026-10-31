using System.Globalization;
using System.Threading.RateLimiting;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Http.RateLimiting;

/// <summary>
/// The rate-limit policies an endpoint picks one of with <c>RequireRateLimiting</c>; a rejection is a 429 problem
/// with <c>Retry-After</c>.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Anonymous reads: a fixed window per client IP.</summary>
    public const string Public = "public";

    internal static void Add(IServiceCollection services)
    {
        services
            .AddOptionsWithValidateOnStart<RateLimitOptions, RateLimitOptionsValidator>()
            .BindConfiguration(RateLimitOptions.SectionName);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;
            // TODO: add the per-tenant GlobalLimiter ceiling when authentication provides a tenant (no session
            // exists yet)
            options.AddPolicy(
                Public,
                static context =>
                {
                    RateLimitOptions limits = context
                        .RequestServices.GetRequiredService<IOptions<RateLimitOptions>>()
                        .Value;
                    // TODO: partition by the client IP from trusted forwarded headers (deferred: infrastructure;
                    // behind a proxy every caller shares one partition until then)
                    string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(
                        clientIp,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.PublicPermitLimit,
                            Window = limits.PublicWindow,
                            QueueLimit = 0,
                        }
                    );
                }
            );
        });
    }

    private static async ValueTask WriteRejectionAsync(
        OnRejectedContext rejected,
        CancellationToken cancellationToken
    )
    {
        HttpContext context = rejected.HttpContext;
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            context.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);
        }

        await context
            .RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = ProblemMapper.Create(
                        context,
                        StatusCodes.Status429TooManyRequests,
                        ProblemTypes.RateLimited,
                        [PlatformErrors.RateLimitExceeded]
                    ),
                }
            );
    }
}
