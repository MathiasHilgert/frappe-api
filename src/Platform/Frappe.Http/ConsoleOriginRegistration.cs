using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Frappe.Http;

/// <summary>
/// Registers the console's origins (<see cref="ConsoleOriginOptions"/>): their validation at startup, the credentialed CORS
/// policy <see cref="ConsolePolicy"/> and the counter of the <see cref="ConsoleOriginCheck"/> refusals.
/// </summary>
public static class ConsoleOriginRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Binds <c>Http:Console:Origins</c> (empty by default; an entry that is not an origin fails at startup), registers the
        /// <see cref="ConsolePolicy"/> CORS policy for those origins only, and <see cref="ConsoleOriginMetrics"/>.
        /// </summary>
        public IServiceCollection AddConsoleOrigins()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddOptions<ConsoleOriginOptions>().BindConfiguration(ConsoleOriginOptions.Section).ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ConsoleOriginOptions>, ConsoleOriginOptionsValidator>());
            services.AddMetrics();
            services.TryAddSingleton<ConsoleOriginMetrics>();
            services.AddCors();
            services.AddOptions<CorsOptions>().Configure<IOptions<ConsoleOriginOptions>>((options, console) => options.AddPolicy(ConsolePolicy, policy => policy
                .WithOrigins([.. console.Value.Allowed()])
                .AllowCredentials()
                .WithMethods(HttpMethods.Get, HttpMethods.Head, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete)
                .AllowAnyHeader()
                .WithExposedHeaders(HeaderNames.Location, HeaderNames.RetryAfter, HeaderNames.ContentLanguage, HeaderNames.ETag, ReplayedHeaderName)
                .SetPreflightMaxAge(PreflightMaxAge)));
            return services;
        }
    }

    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Adds CORS, which answers a preflight and decorates the answers of the endpoints that opt in with
        /// <c>RequireCors(</c><see cref="ConsolePolicy"/><c>)</c>; every answer of such an endpoint varies by <c>Origin</c>.
        /// Call it right after routing, so a preflight is answered before anything else counts it.
        /// </summary>
        public IApplicationBuilder UseConsoleCors()
        {
            ArgumentNullException.ThrowIfNull(app);
            return app.Use(VaryByOriginAsync).UseCors();
        }
    }

    /// <summary>
    /// Name of the CORS policy of the API: credentialed answers for the console's origins
    /// (<see cref="ConsoleOriginOptions"/>) only; any other origin gets no CORS header at all.
    /// </summary>
    public const string ConsolePolicy = "console";

    /// <summary>How long a browser may keep a preflight answer: a console page makes many calls, the list rarely changes.</summary>
    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>The response header that tells a client its idempotent request was answered from a stored outcome.</summary>
    private const string ReplayedHeaderName = "Idempotent-Replayed";

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
}
