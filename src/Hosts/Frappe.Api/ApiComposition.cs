using System.Text.Json.Serialization;
using Frappe.Caching;
using Frappe.Geo.Countries.Argentina;
using Frappe.Geo.Http;
using Frappe.Geo.Infrastructure;
using Frappe.Http;
using Frappe.Observability;
using Frappe.Observability.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace Frappe.Api;

/// <summary>
/// Everything the public API serves: Geo's use cases over HTTP, in the negotiated language and anonymous rate limiting, with the OpenAPI document and its Scalar reference.
/// Every setting is bound to an options type and validated when the host starts, before it serves anything: a missing
/// or invalid one fails naming its configuration key. Optional: <c>Caching:InstanceName</c> (default <c>frappe</c>),
/// the cache key and backplane prefix, for sharing one Valkey between environments; <c>Http:RateLimit</c>; and
/// <c>Http:ForwardedHeaders</c>.
/// </summary>
public static class ApiComposition
{
    public static WebApplicationBuilder AddFrappeApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddFrappeTelemetry(
            "frappe-api",
            traces => traces.AddAspNetCoreInstrumentation(options => options.Filter = context => !IsInfrastructurePath(context.Request.Path)),
            meters => meters.AddAspNetCoreInstrumentation());

        // An outgoing request whose URL carries personal data (Places Details: the place id) shows a redacted URL in traces (S15).
        builder.Services.Configure<HttpClientTraceInstrumentationOptions>(options => options.EnrichWithHttpRequestMessage += OutgoingRequestTelemetry.Redact);
        builder.Services.AddOptions<TrustedProxyOptions>().BindConfiguration(TrustedProxyOptions.Section).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<TrustedProxyOptions>, TrustedProxyOptionsValidator>();
        builder.Services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<TrustedProxyOptions>>((options, trusted) => ConfigureForwardedHeaders(options, trusted.Value));
        builder.Services
            .AddFrappeHttp()
            .AddFrappeCaching()
            .AddGeoCore()
            .AddArgentinaCalendar()
            .AddGeoHttp()
            .AddOpenApi("v1", options => options
                .AddDocumentTransformer<ApiDocumentInfo>()
                .AddDocumentTransformer<ProblemDocumentation>()
                .AddOperationTransformer<ProblemDocumentation>()
                .AddOperationTransformer<AcceptLanguageDocumentation>()
                .AddOperationTransformer<CachingDocumentation>()
                .AddOperationTransformer<ParameterDocumentation>()
                .AddOperationTransformer<LocationDocumentation>()
                .AddSchemaTransformer<StringEnumDocumentation>());

        // The web defaults read numbers from JSON strings, which the schema generator then documents as
        // ["number", "string"]. Numbers are numbers in both directions, in the few bodies the API reads too.
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

        return builder;
    }

    public static WebApplication MapFrappeApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // First, so the rate limiter and the logs see the client behind a trusted proxy, never the proxy itself.
        app.UseForwardedHeaders();
        app.UseFrappeHttp();
        app.MapFrappeHealth();
        app.MapOpenApi().AnswerHead();
        app.MapScalarApiReference("/docs", options => options.WithTitle("Frappé API")).AnswerHead();

        // Every /v1 endpoint may be rate limited, so the group declares the 429 once for all of them.
        app.MapGroup("/v1")
            .AnswerHead()
            .NegotiateRequestLanguage()
            .RequireRateLimiting(HttpRegistration.AnonymousPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .MapGeoEndpoints();

        return app;
    }

    /// <summary>Probes, OpenAPI and its reference are not traced: they would drown the request traces that matter.</summary>
    private static bool IsInfrastructurePath(PathString path) =>
        path.StartsWithSegments(HealthEndpoints.Prefix) || path.StartsWithSegments("/openapi") || path.StartsWithSegments("/docs");

    /// <summary>
    /// <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are trusted only from the proxies and networks in
    /// <c>Http:ForwardedHeaders:KnownProxies</c> and <c>KnownNetworks</c> (comma separated, <see cref="TrustedProxyOptions"/>).
    /// With none configured the headers are ignored, so a client can never spoof its address to dodge the rate limiter.
    /// </summary>
    private static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, TrustedProxyOptions trusted)
    {
        var proxies = trusted.Proxies();
        var networks = trusted.Networks();
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        // ASP.NET Core trusts every sender when both lists are empty, so with nothing configured the headers are
        // not processed at all.
        if (proxies.Length == 0 && networks.Length == 0)
        {
            options.ForwardedHeaders = ForwardedHeaders.None;
            return;
        }

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Walk the whole chain of trusted proxies: the walk still stops at the first hop that is not trusted, so a
        // client cannot forge its way past it, and two proxies in a row no longer put every client in one bucket.
        options.ForwardLimit = null;
        foreach (var proxy in proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }
}
