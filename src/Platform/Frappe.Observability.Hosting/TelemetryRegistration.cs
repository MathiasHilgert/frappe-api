using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Frappe.Observability.Hosting;

/// <summary>
/// Everything a host exports, wired once: traces, metrics and logs of the platform and of every
/// <c>Frappe.*</c> source and meter, plus HttpClient, Npgsql, FusionCache, runtime and process telemetry. Modules add
/// nothing: they emit through the platform or declare business meters named <c>Frappe.&lt;Module&gt;</c>.
/// </summary>
public static class TelemetryRegistration
{
    /// <param name="builder">The host.</param>
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Adds OpenTelemetry to a host. The OTLP exporter is enabled only when an OTLP endpoint is set
        /// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, or a per-signal <c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c> and the like),
        /// so tests and plain local runs export nothing. The standard <c>OTEL_*</c> variables apply (service name, protocol,
        /// resource attributes), except the sampler: it is parent-based and records every root span, because the collector
        /// decides which traces to keep.
        /// </summary>
        /// <param name="serviceName">Service name when <c>OTEL_SERVICE_NAME</c> is not set, for example <c>frappe-api</c>.</param>
        /// <param name="tracing">Host-specific trace sources or instrumentation (ASP.NET Core, Quartz, NATS).</param>
        /// <param name="metrics">Host-specific meters or instrumentation.</param>
        public IHostApplicationBuilder AddFrappeTelemetry(string serviceName,
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
                    // Parent-based: a root span is always recorded and sent to the collector, which keeps errors, slow traces and
                    // a share of the rest (OBS-13); a child follows its parent's decision, so a trace is never cut in half.
                    traces
                        .SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))
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
