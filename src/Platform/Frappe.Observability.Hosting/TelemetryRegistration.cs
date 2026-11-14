using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ZiggyCreatures.Caching.Fusion.OpenTelemetry;

namespace Frappe.Observability.Hosting;

/// <summary>
/// Everything a host exports, wired once (S19, S22): traces, metrics and logs of the platform and of every
/// <c>Frappe.*</c> source and meter, plus HttpClient, Npgsql, FusionCache, runtime and process telemetry. Modules add
/// nothing: they emit through the platform or declare business meters named <c>Frappe.&lt;Module&gt;</c>.
/// </summary>
public static class TelemetryRegistration
{
    /// <summary>
    /// Adds OpenTelemetry to a host. The OTLP exporter is enabled only when an OTLP endpoint is set
    /// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, or a per-signal <c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c> and the like),
    /// so tests and plain local runs export nothing. The standard <c>OTEL_*</c> variables apply (service name, sampler,
    /// protocol, resource attributes); without a sampler every trace is kept.
    /// </summary>
    /// <param name="builder">The host.</param>
    /// <param name="serviceName">Service name when <c>OTEL_SERVICE_NAME</c> is not set, for example <c>frappe-api</c>.</param>
    /// <param name="tracing">Host-specific trace sources or instrumentation (ASP.NET Core, Quartz, NATS).</param>
    /// <param name="metrics">Host-specific meters or instrumentation.</param>
    public static IHostApplicationBuilder AddFrappeTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName,
        Action<TracerProviderBuilder>? tracing = null,
        Action<MeterProviderBuilder>? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var configuration = builder.Configuration;
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } configured ? configured : serviceName, serviceVersion: version)
                .AddEnvironmentVariableDetector())
            .WithTracing(traces =>
            {
                traces
                    .AddSource(FrappeTelemetry.Wildcard)
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddFusionCacheInstrumentation();
                tracing?.Invoke(traces);
            })
            .WithMetrics(meters =>
            {
                meters
                    .AddMeter(FrappeTelemetry.Wildcard)
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddNpgsqlInstrumentation()
                    .AddFusionCacheInstrumentation();
                metrics?.Invoke(meters);
            })
            .WithLogging(_ => { }, logs =>
            {
                logs.IncludeFormattedMessage = true;
                logs.IncludeScopes = true;
            });

        if (IsExportConfigured(configuration))
        {
            telemetry.UseOtlpExporter();
        }

        return builder;
    }

    private static readonly string[] EndpointKeys =
    [
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
    ];

    private static bool IsExportConfigured(IConfiguration configuration) =>
        EndpointKeys.Any(key => !string.IsNullOrWhiteSpace(configuration[key]));
}
