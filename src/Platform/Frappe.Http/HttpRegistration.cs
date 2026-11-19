using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Http;

/// <summary>
/// The HTTP conventions every Frappé API shares. This is the entry point a host calls; each convention registers itself
/// in a file of its own (<see cref="ProblemDetailsRegistration"/> for the Problem Details contract of
/// <see cref="FrappeProblemDetails"/>, shared by the errors that come from a use case and by the ones that do not: a
/// rejected request, an unexpected exception).
/// </summary>
public static class HttpRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the HTTP conventions: the framework's Problem Details writer with the Frappé contract, so every
        /// problem is shaped the same whoever raises it; rate limiting (<see cref="RateLimitingRegistration"/>); idempotent
        /// replays (<see cref="IdempotencyRegistration"/>); the proxies trusted to forward the client's address
        /// (<see cref="TrustedProxyOptions"/>); the console's origins with their CORS policy
        /// (<see cref="ConsoleOriginOptions"/>); and the API document (<see cref="OpenApiRegistration"/>).
        /// </summary>
        public IServiceCollection AddFrappeHttp()
        {
            ArgumentNullException.ThrowIfNull(services);
            return services
                .AddFrappeProblemDetails()
                .AddFrappeRateLimiting()
                .AddFrappeIdempotency()
                .AddTrustedProxies()
                .AddConsoleOrigins()
                .AddFrappeOpenApi();
        }
    }

    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Adds the shared middleware and routing: a request the framework rejects while reading it
        /// (<see cref="Microsoft.AspNetCore.Http.BadHttpRequestException"/>) keeps its <c>4xx</c>, a database that does not answer
        /// (<see cref="DatabaseOutage"/>) becomes a <c>503</c> <see cref="DatabaseUnavailableCode"/> with <c>Retry-After</c>,
        /// any other exception becomes a <c>500</c> (both logged by the middleware, never leaking the exception), and any
        /// <c>4xx</c> answered without a body (<c>400</c>, <c>404</c>, <c>405</c>, <c>413</c>, <c>415</c>, ...) gets one, all
        /// as Problem Details; the client's address comes from a trusted proxy only; a <c>HEAD</c> answers without content;
        /// then routing, the console's CORS policy (before rate limiting, so a <c>429</c> still reaches the console) and rate
        /// limiting, which needs the matched endpoint. Call it before mapping endpoints.
        /// </summary>
        public IApplicationBuilder UseFrappeHttp()
        {
            ArgumentNullException.ThrowIfNull(app);
            return app
                .UseTrustedProxies()
                .UseFrappeProblemDetails()
                .Use(HeadRequests.DiscardBodyAsync)
                .UseRouting()
                .UseConsoleCors()
                .UseRateLimiter();
        }
    }

    /// <summary>Name of the rate limiting policy for endpoints anyone may call without credentials.</summary>
    public const string AnonymousPolicy = "anonymous";

    /// <summary>The client spent a rate limit: it is told how long to wait.</summary>
    public const string RateLimitedCode = "http.rate_limited";

    /// <summary>The request failed for a reason the client cannot fix; the exception is logged, never answered.</summary>
    public const string UnexpectedCode = "http.unexpected";

    /// <summary>The service answered <c>503</c> without saying why: the client may try again later.</summary>
    public const string UnavailableCode = "http.unavailable";

    /// <summary>The URL matches no endpoint.</summary>
    public const string NotFoundCode = "http.not_found";

    /// <summary>The route exists but does not answer the method.</summary>
    public const string MethodNotAllowedCode = "http.method_not_allowed";

    /// <summary>The framework could not read the request.</summary>
    public const string BadRequestCode = "http.bad_request";

    /// <summary>The request body is larger than the server accepts.</summary>
    public const string RequestTooLargeCode = "http.request_too_large";

    /// <summary>The request body is in a media type the endpoint does not read.</summary>
    public const string UnsupportedMediaTypeCode = "http.unsupported_media_type";

    /// <summary>
    /// The database did not answer (<see cref="DatabaseOutage"/>): a <c>503</c> with <c>Retry-After</c> of
    /// <see cref="DatabaseUnavailableRetryAfterSeconds"/>, whatever module's request it was.
    /// </summary>
    public const string DatabaseUnavailableCode = "platform.database.unavailable";

    /// <summary>How long a client is asked to wait after <see cref="DatabaseUnavailableCode"/>: the same as any unavailable dependency.</summary>
    public const int DatabaseUnavailableRetryAfterSeconds = ErrorsProblemHttpResult.UnavailableRetryAfterSeconds;
}
