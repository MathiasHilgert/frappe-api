using Frappe.Identity.Infrastructure.Security;
using Frappe.Platform.Health;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Frappe.Api;

internal static class Observability
{
    /// <summary>
    /// Traces, metrics and logs over OTLP. The endpoint comes from the standard
    /// OTEL_EXPORTER_OTLP_ENDPOINT variable, so the binary is the same in every environment.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("frappe-api"))
            .WithTracing(tracing => tracing
                // Probes hit every few seconds; tracing them only buries real requests.
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource("Wolverine"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation()
                .AddMeter("Microsoft.EntityFrameworkCore")
                .AddMeter("Wolverine")
                .AddMeter(HealthMetricsPublisher.MeterName)
                .AddMeter(IdentityMetrics.MeterName))
            .UseOtlpExporter();

        return builder;
    }
}
