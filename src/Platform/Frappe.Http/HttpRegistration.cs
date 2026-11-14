using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>How many requests one client may make per window on anonymous endpoints.</summary>
public sealed record AnonymousRateLimit(int PermitLimit, TimeSpan Window)
{
    public static AnonymousRateLimit Default { get; } = new(120, TimeSpan.FromMinutes(1));
}

/// <summary>
/// The HTTP conventions every Frappé API shares: rate limiting, and the same Problem Details contract
/// (<see cref="FrappeProblemDetails"/>) for errors that do not come from a use case (a rejected request, an unexpected
/// exception) as for the ones that do.
/// </summary>
public static class HttpRegistration
{
    /// <summary>Name of the rate limiting policy for endpoints anyone may call without credentials.</summary>
    public const string AnonymousPolicy = "anonymous";

    public const string RateLimitedCode = "http.rate_limited";
    public const string UnexpectedCode = "http.unexpected";
    public const string NotFoundCode = "http.not_found";
    public const string MethodNotAllowedCode = "http.method_not_allowed";
    public const string BadRequestCode = "http.bad_request";
    public const string RequestTooLargeCode = "http.request_too_large";
    public const string UnsupportedMediaTypeCode = "http.unsupported_media_type";

    /// <summary>
    /// Registers the framework's Problem Details writer with the Frappé contract, and the <see cref="AnonymousPolicy"/>
    /// rate limiter: a fixed window per client address, with no queue, answering <c>429</c> with <c>Retry-After</c>
    /// once the window is spent. The limit is bound from <c>Http:RateLimit</c> (<see cref="AnonymousRateLimitOptions"/>);
    /// fewer than one permit or a window that is not positive fails at startup naming the key.
    /// </summary>
    public static IServiceCollection AddFrappeHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<AnonymousRateLimitOptions>()
            .BindConfiguration(AnonymousRateLimitOptions.Section)
            .Validate(
                options => options.PermitLimit >= 1,
                $"Configuration '{AnonymousRateLimitOptions.Section}:{nameof(AnonymousRateLimitOptions.PermitLimit)}' must be at least 1.")
            .Validate(
                options => options.Window > TimeSpan.Zero,
                $"Configuration '{AnonymousRateLimitOptions.Section}:{nameof(AnonymousRateLimitOptions.Window)}' must be positive.")
            .ValidateOnStart();
        return services.AddFrappeHttpServices();
    }

    /// <summary>The same as <see cref="AddFrappeHttp(IServiceCollection)"/>, with the limit given in code.</summary>
    public static IServiceCollection AddFrappeHttp(this IServiceCollection services, AnonymousRateLimit anonymous)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(anonymous);
        ArgumentOutOfRangeException.ThrowIfLessThan(anonymous.PermitLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(anonymous.Window, TimeSpan.Zero);
        services.AddOptions<AnonymousRateLimitOptions>().Configure(options =>
        {
            options.PermitLimit = anonymous.PermitLimit;
            options.Window = anonymous.Window;
        });
        return services.AddFrappeHttpServices();
    }

    private static IServiceCollection AddFrappeHttpServices(this IServiceCollection services)
    {
        services.AddFrappeProblemDetails().AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<AnonymousRateLimitOptions>>((options, limit) =>
        {
            var anonymous = limit.Value;
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AnonymousPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = anonymous.PermitLimit,
                    Window = anonymous.Window,
                    QueueLimit = 0,
                }));
            options.OnRejected = (context, _) =>
            {
                // A fixed window always knows when it reopens; the whole window is the safe answer if it ever does not.
                var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : anonymous.Window;
                var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

                return new ValueTask(PlatformProblem.WriteAsync(
                    context.HttpContext,
                    PlatformProblem.Of(ProblemCategory.TooManyRequests, RateLimitedCode, new Dictionary<string, object?> { ["retryAfter"] = seconds })));
            };
        });
        return services;
    }

    /// <summary>
    /// Adds the shared middleware and routing: a request the framework rejects while reading it
    /// (<see cref="BadHttpRequestException"/>) keeps its <c>4xx</c>, any other exception becomes a <c>500</c> (logged by
    /// the middleware, never leaking the exception), any <c>4xx</c> answered without a body (<c>400</c>, <c>404</c>,
    /// <c>405</c>, <c>413</c>, <c>415</c>, ...) gets one, all as Problem Details; a HEAD response's body is discarded; then
    /// routing and rate limiting, which needs the matched endpoint. Call it before mapping endpoints; HEAD is declared
    /// per group with <see cref="HeadRequests.AnswerHead"/>.
    /// </summary>
    public static IApplicationBuilder UseFrappeHttp(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app
            .UseExceptionHandler(new ExceptionHandlerOptions
            {
                StatusCodeSelector = exception => exception is BadHttpRequestException rejected
                    ? rejected.StatusCode
                    : StatusCodes.Status500InternalServerError,
                ExceptionHandler = httpContext => PlatformProblem.WriteAsync(
                    httpContext, PlatformProblem.ForStatus(httpContext, httpContext.Response.StatusCode)),
            })
            .UseStatusCodePages(StatusCodeProblems.WriteAsync)
            .Use(HeadRequests.DiscardBodyAsync)
            .UseRouting()
            .UseRateLimiter();
    }
}
