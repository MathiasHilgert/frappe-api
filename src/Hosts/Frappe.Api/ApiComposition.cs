using System.Text.Json.Serialization;
using Frappe.Caching;
using Frappe.Http;
using Frappe.Http.OpenApi;
using Frappe.Modules;
using Frappe.Observability;
using Frappe.Observability.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace Frappe.Api;

/// <summary>
/// Everything the public API serves: the modules <see cref="Modules.Services"/> and <see cref="Modules.Endpoints"/> list, under
/// <c>/v1</c> with the platform's conventions (negotiated language, anonymous rate limiting counted in Valkey across replicas, and
/// each replica counting on its own while Valkey is down), the health probes, and the OpenAPI document with its Scalar reference.
/// Every setting is bound to an options type and validated when the host starts, before it serves anything: a missing or invalid
/// one fails naming its configuration key. Optional: <c>Caching:InstanceName</c> (default <c>frappe</c>), the cache key and
/// backplane prefix, for sharing one Valkey between environments; <c>Http:RateLimit</c>; <c>Http:ForwardedHeaders</c>; and
/// <c>Http:OpenApi</c>, the title, version and description of the document. <c>Http:Console:Origins</c> (comma separated, empty
/// by default) names the console's origins: the only ones that get credentialed CORS answers and may send unsafe requests with
/// the session cookie. <c>Identity:Sessions:IdleTimeout</c> (default 12 hours) and <c>AbsoluteTimeout</c> (default 30 days) set how long a
/// session lives.
/// </summary>
public static class ApiComposition
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>Composes the Api with the modules of <see cref="Modules"/>.</summary>
        public WebApplicationBuilder AddFrappeApi() => builder.AddFrappeApi(Modules.Services, Modules.Endpoints);

        /// <summary>Composes the Api with the given modules, which tests replace to prove the composition.</summary>
        public WebApplicationBuilder AddFrappeApi(IReadOnlyCollection<IModule> modules, IReadOnlyCollection<IHttpModule> endpoints)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(modules);
            ArgumentNullException.ThrowIfNull(endpoints);

            builder.AddFrappeTelemetry(
                "frappe-api",
                traces => traces.AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context => !IsInfrastructurePath(context.Request.Path);

                    // A query that carries a credential (the one-click unsubscribe token) shows redacted in traces (SEC-09).
                    options.EnrichWithHttpRequest = RequestTelemetry.RedactQuery;
                }),
                meters => meters.AddAspNetCoreInstrumentation());

            // An outgoing request whose URL carries personal data shows a redacted URL in traces.
            builder.Services.Configure<HttpClientTraceInstrumentationOptions>(
                options => options.EnrichWithHttpRequestMessage += OutgoingRequestTelemetry.Redact);
            builder.Services
                .AddFrappeHttp()
                .AddFrappeCaching()
                .AddFrappeOpenApi();

            // No default scheme (SEC-01): a module that authenticates registers its scheme, and only routes that ask for it use it.
            builder.Services.AddAuthentication();
            builder.Services.AddAuthorization();

            foreach (var module in modules)
            {
                module.AddServices(builder.Services);
            }

            builder.Services.AddHttpModules(endpoints);

            // The web defaults read numbers from JSON strings, which the schema generator then documents as
            // ["number", "string"]. Numbers are numbers in both directions, in the few bodies the API reads too.
            builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

            return builder;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>Maps the Api with the modules of <see cref="Modules.Endpoints"/>.</summary>
        public WebApplication MapFrappeApi() => app.MapFrappeApi(Modules.Endpoints);

        /// <summary>Maps the Api with the given modules' endpoints, which tests replace to prove the composition.</summary>
        public WebApplication MapFrappeApi(IReadOnlyCollection<IHttpModule> endpoints)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(endpoints);

            // Forwarded headers come first inside, so the rate limiter and the logs see the client behind a trusted proxy.
            app.UseFrappeHttp();

            // Authentication, then the console origin check for the browser credentials the modules registered (Identity's session
            // cookie), then authorization: the origin answer
            // outweighs a 401, and a bearer request is never origin-checked (SEC-03).
            app.UseAuthentication();
            app.UseConsoleOriginCheck();
            app.UseAuthorization();
            app.MapFrappeHealth();
            app.MapOpenApi().AnswerHead();
            var title = app.Services.GetRequiredService<IOptions<ApiDocumentOptions>>().Value.Title;
            app.MapScalarApiReference("/docs", options => options.WithTitle(title)).AnswerHead();
            app.MapHttpModules(endpoints);
            return app;
        }
    }

    /// <summary>Probes, OpenAPI and its reference are not traced: they would drown the request traces that matter.</summary>
    private static bool IsInfrastructurePath(PathString path) =>
        path.StartsWithSegments(HealthEndpoints.Prefix) || path.StartsWithSegments("/openapi") || path.StartsWithSegments("/docs");
}
