using System.Threading.RateLimiting;
using Frappe.Platform.Web.Forwarding;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Web.Security;

public static class RateLimitingRegistration
{
    /// <summary>Endpoint policy for anonymous auth endpoints: a fixed window per client IP.</summary>
    public const string AnonymousAuthPolicy = "anonymous-auth";

    public static IServiceCollection AddAnonymousRateLimiting(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(AnonymousAuthPolicy, http =>
            {
                var options = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(http.ClientIp(), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0,
                });
            });
            limiter.OnRejected = (context, _) =>
            {
                var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? retryAfter
                    : TimeSpan.FromSeconds(1);
                return new ValueTask(Result.Failure(PlatformErrors.RateLimited.WithRetryAfter(wait)).ToProblem()
                    .ExecuteAsync(context.HttpContext));
            };
        });
        return services;
    }

    /// <summary>
    /// The one convention for an endpoint reachable without a session (sign-in, sign-up, password reset): it opts out
    /// of deny-by-default, is rate limited per IP by the in-process backstop, and documents the 429 that can answer.
    /// </summary>
    public static TBuilder AllowAnonymousAuth<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AllowAnonymous()
            .RequireRateLimiting(AnonymousAuthPolicy)
            .WithMetadata(new ProducesResponseTypeMetadata(
                StatusCodes.Status429TooManyRequests, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails), ["application/problem+json"]));
}
