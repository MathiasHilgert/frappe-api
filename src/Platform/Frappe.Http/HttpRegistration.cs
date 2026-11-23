using System.Globalization;
using System.Threading.RateLimiting;
using Frappe.Http.Idempotency;
using Frappe.Http.RateLimiting;
using Frappe.Http.TrustedProxies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Frappe.Http;

/// <summary>
/// The HTTP conventions every Frappé API shares: rate limiting, and the same Problem Details contract
/// (<see cref="FrappeProblemDetails"/>) for errors that do not come from a use case (a rejected request, an unexpected
/// exception) as for the ones that do.
/// </summary>
public static class HttpRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the framework's Problem Details writer with the Frappé contract, and the <see cref="AnonymousPolicy"/>
        /// rate limiter: a fixed window per client address, with no queue, answering <c>429</c> with <c>Retry-After</c>
        /// once the window is spent. The limit is bound from <c>Http:RateLimit</c> (<see cref="AnonymousRateLimitOptions"/>);
        /// fewer than one permit or a window that is not positive fails at startup naming the key. With an
        /// <see cref="IRateLimitStore"/> registered, every replica counts the same windows there; while it fails, each replica
        /// counts on its own again. The console's origins are bound from <c>Http:Console:Origins</c>
        /// (<see cref="ConsoleOriginOptions"/>), empty by default; an entry that is not an origin fails at startup. The origin
        /// check's refusals are counted (<see cref="ConsoleOriginMetrics"/>). The Valkey stores of the rate limit and of
        /// <c>Idempotency-Key</c> answers are registered here (they need <c>AddFrappeCaching</c>), and the proxies trusted to
        /// forward the client's address are bound from <c>Http:ForwardedHeaders</c> (<see cref="TrustedProxyOptions"/>).
        /// </summary>
        public IServiceCollection AddFrappeHttp()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddOptions<ConsoleOriginOptions>().BindConfiguration(ConsoleOriginOptions.Section).ValidateOnStart();
            services.AddOptions<AnonymousRateLimitOptions>()
                .BindConfiguration(AnonymousRateLimitOptions.Section)
                .ValidateOnStart();

            // Exception to CFG-03: the key the message names is the policy's own section, known only when the policy is registered.
            services.AddSingleton<IValidateOptions<AnonymousRateLimitOptions>>(
                new AnonymousRateLimitOptionsValidator(Options.DefaultName, AnonymousRateLimitOptions.Section));

            // Every replica counts the same windows and replays the same answers; a host or a test that registered its own keeps it.
            services.TryAddSingleton<IRateLimitStore, ValkeyRateLimitStore>();
            services.TryAddSingleton<IIdempotencyStore, ValkeyIdempotencyStore>();
            return services.AddFrappeForwardedHeaders().AddFrappeHttpServices();
        }

        /// <summary>The same as <see cref="AddFrappeHttp(IServiceCollection)"/>, with the limit given in code and no console origin.</summary>
        public IServiceCollection AddFrappeHttp(AnonymousRateLimit anonymous)
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

        /// <summary>
        /// Adds a per-client rate limiting policy named <paramref name="policy"/> next to <see cref="AnonymousPolicy"/>, for
        /// endpoints that need a limit of their own (an endpoint that requires it is counted by it instead of by the group's
        /// policy): the same fixed window per client address, with no queue and the same <c>429</c>, counted under its own
        /// name in the <see cref="IRateLimitStore"/> when one is registered. The limit is <paramref name="defaults"/>, unless
        /// <paramref name="section"/> sets <c>PermitLimit</c> or <c>Window</c>; fewer than one permit or a window that is not
        /// positive fails at startup naming the key.
        /// </summary>
        public IServiceCollection AddFrappeRateLimitPolicy(string policy, string section, AnonymousRateLimit defaults)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(policy);
            ArgumentException.ThrowIfNullOrWhiteSpace(section);
            ArgumentNullException.ThrowIfNull(defaults);
            ArgumentOutOfRangeException.ThrowIfLessThan(defaults.PermitLimit, 1);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(defaults.Window, TimeSpan.Zero);
            if (string.Equals(policy, AnonymousPolicy, StringComparison.Ordinal))
            {
                throw new ArgumentException($"The policy name '{AnonymousPolicy}' is the platform's own.", nameof(policy));
            }

            services.AddOptions<AnonymousRateLimitOptions>(policy)
                .Configure(options =>
                {
                    options.PermitLimit = defaults.PermitLimit;
                    options.Window = defaults.Window;
                })
                .BindConfiguration(section)
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<AnonymousRateLimitOptions>>(new AnonymousRateLimitOptionsValidator(policy, section));
            services.AddRateLimitStoreAccess();
            services.AddOptions<RateLimiterOptions>().Configure<IOptionsMonitor<AnonymousRateLimitOptions>, RateLimitStoreAccess>(
                (options, limits, access) => AddClientPolicy(options, policy, limits.Get(policy), access.Store));
            return services;
        }

        private IServiceCollection AddFrappeHttpServices()
        {
            services.TryAddSingleton(TimeProvider.System);
            return services.AddPlatformErrorMessages()
                .AddFrappeProblemDetails()
                .AddFrappeIdempotency()
                .AddFrappeConsoleCors()
                .AddFrappeRateLimiting();
        }

        /// <summary>A host that registers its own store before this call (one every replica shares) keeps it.</summary>
        private IServiceCollection AddFrappeIdempotency()
        {
            services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
            return services;
        }

        private IServiceCollection AddFrappeConsoleCors()
        {
            services.AddMetrics();
            services.TryAddSingleton<ConsoleOriginMetrics>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ConsoleOriginOptions>, ConsoleOriginOptionsValidator>());
            services.AddCors();
            services.AddOptions<CorsOptions>().Configure<IOptions<ConsoleOriginOptions>>((options, console) =>
                options.AddPolicy(ConsolePolicy, policy => policy
                .WithOrigins([.. console.Value.Allowed()])
                .AllowCredentials()
                .WithMethods(HttpMethods.Get, HttpMethods.Head, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete)
                .AllowAnyHeader()
                .WithExposedHeaders(HeaderNames.Location, HeaderNames.RetryAfter, HeaderNames.ContentLanguage, HeaderNames.ETag,
                    IdempotencyKey.ReplayedHeaderName)
                .SetPreflightMaxAge(PreflightMaxAge)));
            return services;
        }

        private IServiceCollection AddFrappeRateLimiting()
        {
            services.AddRateLimiter(_ => { });
            services.AddRateLimitStoreAccess();
            services.AddOptions<RateLimiterOptions>().Configure<IOptions<AnonymousRateLimitOptions>, RateLimitStoreAccess>((options, limit, access) =>
            {
                var anonymous = limit.Value;
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                AddClientPolicy(options, AnonymousPolicy, anonymous, access.Store);
                options.OnRejected = (context, _) =>
                {
                    // A fixed window always knows when it reopens; the whole window is the safe answer if it ever does not.
                    var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : anonymous.Window;
                    var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
                    context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

                    return new ValueTask(PlatformProblem.WriteAsync(
                        context.HttpContext,
                        PlatformProblem.Of(ProblemCategory.TooManyRequests, RateLimitedCode, new Dictionary<string,
                            object?>
                        { ["retryAfter"] = seconds })));
                };
            });
            return services;
        }

        /// <summary>One shared store per process, so every policy shares its timeout and breaker.</summary>
        private void AddRateLimitStoreAccess()
        {
            services.AddMetrics();
            services.TryAddSingleton<RateLimitMetrics>();
            services.TryAddSingleton(provider => new RateLimitStoreAccess(SharedStore(provider)));
        }
    }

    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Adds the shared middleware and routing: the forwarded client address (trusted proxies only), a request the framework rejects while
        /// reading it
        /// (<see cref="BadHttpRequestException"/>) keeps its <c>4xx</c>, a database that does not answer
        /// (<see cref="DatabaseOutage"/>) becomes a <c>503</c> <see cref="DatabaseUnavailableCode"/> with <c>Retry-After</c>,
        /// any other exception becomes a <c>500</c> (both logged by the middleware, never leaking the exception), any <c>4xx</c> answered without a
        /// body (<c>400</c>, <c>404</c>,
        /// <c>405</c>, <c>413</c>, <c>415</c>, ...) gets one, all as Problem Details; a HEAD response's body is discarded; then
        /// routing and rate limiting, which needs the matched endpoint. Call it before mapping endpoints; HEAD is declared
        /// per group with <see cref="HeadRequests.AnswerHead"/>. CORS runs right after routing, so a preflight is answered
        /// before anything is counted and every answer, a 429 included, carries the console's CORS headers; endpoints opt in
        /// with <c>RequireCors(</c><see cref="ConsolePolicy"/><c>)</c>, and every answer of such an endpoint varies by
        /// <c>Origin</c>.
        /// </summary>
        public IApplicationBuilder UseFrappeHttp()
        {
            ArgumentNullException.ThrowIfNull(app);
            // First, so the rate limiter and the logs see the client behind a trusted proxy, never the proxy itself.
            return app
                .UseForwardedHeaders()
                .UseExceptionHandler(new ExceptionHandlerOptions
                {
                    StatusCodeSelector = exception => exception switch
                    {
                        BadHttpRequestException rejected => rejected.StatusCode,
                        _ when DatabaseOutage.Caused(exception) => StatusCodes.Status503ServiceUnavailable,
                        _ => StatusCodes.Status500InternalServerError,
                    },
                    ExceptionHandler = WriteExceptionProblemAsync,
                })
                .UseStatusCodePages(StatusCodeProblems.WriteAsync)
                .Use(HeadRequests.DiscardBodyAsync)
                .UseRouting()
                .Use(VaryByOriginAsync)
                .UseCors()
                .UseRateLimiter();
        }
    }

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
    /// The database did not answer (<see cref="DatabaseOutage"/>): a <c>503</c> with <c>Retry-After</c> of
    /// <see cref="DatabaseUnavailableRetryAfterSeconds"/>, whatever module's request it was.
    /// </summary>
    public const string DatabaseUnavailableCode = "platform.database.unavailable";

    /// <summary>How long a client is asked to wait after <see cref="DatabaseUnavailableCode"/>: the same as any unavailable dependency.</summary>
    public const int DatabaseUnavailableRetryAfterSeconds = ErrorsProblemHttpResult.UnavailableRetryAfterSeconds;

    /// <summary>An unsafe request carried a browser credential without a console <c>Origin</c> (<see cref="ConsoleOriginCheck"/>).</summary>
    public const string OriginForbiddenCode = "http.origin.forbidden";

    /// <summary>
    /// Name of the CORS policy of the API: credentialed answers for the console's origins
    /// (<see cref="ConsoleOriginOptions"/>) only; any other origin gets no CORS header at all.
    /// </summary>
    public const string ConsolePolicy = "console";

    /// <summary>How long a browser may keep a preflight answer: a console page makes many calls, the list rarely changes.</summary>
    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// <paramref name="policy"/>: a fixed window per client address, in the shared store under the policy's name when
    /// there is one, else in this replica's memory.
    /// </summary>
    private static void AddClientPolicy(RateLimiterOptions options, string policy, AnonymousRateLimitOptions limit, SharedRateLimitStore? shared)
    {
        var permitLimit = limit.PermitLimit;
        var window = limit.Window;
        options.AddPolicy(policy, httpContext =>
        {
            var client = ClientAddress.Of(httpContext);
            return shared is null
                ? RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0,
                })
                : RateLimitPartition.Get(client, key => new SharedFixedWindowRateLimiter(
                    $"{policy}:{key}", permitLimit, window, shared, shared.Time));
        });
    }

    /// <summary>
    /// The problem of an exception the middleware caught: a database outage (<see cref="DatabaseOutage"/>, read from the
    /// exception itself, never from the status) tells the client when to try again; any other exception is the platform
    /// problem of the status <c>StatusCodeSelector</c> chose, a <c>503</c> included.
    /// </summary>
    private static Task WriteExceptionProblemAsync(HttpContext httpContext)
    {
        if (httpContext.Features.Get<IExceptionHandlerFeature>()?.Error is { } exception && DatabaseOutage.Caused(exception))
        {
            httpContext.Response.Headers.RetryAfter = DatabaseUnavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return PlatformProblem.WriteAsync(
                httpContext,
                PlatformProblem.Of(
                    ProblemCategory.Unavailable,
                    DatabaseUnavailableCode,
                    new Dictionary<string, object?> { ["retryAfter"] = DatabaseUnavailableRetryAfterSeconds }));
        }

        return PlatformProblem.WriteAsync(httpContext, PlatformProblem.ForStatus(httpContext, httpContext.Response.StatusCode));
    }

    /// <summary>
    /// An answer of an endpoint under a CORS policy depends on the request's <c>Origin</c> (CORS headers or none), so it
    /// says <c>Vary: Origin</c>, whatever the origin and however many the policy allows: otherwise a shared cache could
    /// hand the console an answer stored without its CORS headers. The CORS middleware says it itself only when a policy
    /// allows several origins and the request came from one. Response callbacks run last registered first, and this one
    /// is registered ahead of the CORS middleware's, so it runs after it and adds the field only when CORS did not.
    /// </summary>
    private static Task VaryByOriginAsync(HttpContext httpContext, RequestDelegate next)
    {
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<IEnableCorsAttribute>() is not null)
        {
            httpContext.Response.OnStarting(() =>
            {
                var vary = httpContext.Response.Headers.Vary;
                if (!vary.Any(value => value is not null && value.Contains(HeaderNames.Origin, StringComparison.OrdinalIgnoreCase)))
                {
                    httpContext.Response.Headers.Append(HeaderNames.Vary, HeaderNames.Origin);
                }

                return Task.CompletedTask;
            });
        }

        return next(httpContext);
    }

    /// <summary>The registered <see cref="IRateLimitStore"/> behind its timeout and breaker, or none: each replica counts.</summary>
    private static SharedRateLimitStore? SharedStore(IServiceProvider provider) =>
        provider.GetService<IRateLimitStore>() is { } store
            ? new SharedRateLimitStore(
                store,
                provider.GetService<TimeProvider>() ?? TimeProvider.System,
                provider.GetRequiredService<RateLimitMetrics>(),
                provider.GetRequiredService<ILoggerFactory>().CreateLogger<SharedRateLimitStore>())
            : null;

    /// <summary>The process's shared store, if any, created once.</summary>
    private sealed record RateLimitStoreAccess(SharedRateLimitStore? Store);
}
