using System.Reflection;
using Frappe.Platform.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Frappe.Platform;

// The OpenTelemetry baseline every process shares: traces, metrics and logs over OTLP, sharing one trace id.
// Adapters add their own sources and meters (Wolverine's in Messaging); OTEL_* variables configure the export.
internal static class TelemetryExtensions
{
    public const string ServiceName = "frappe";

    public static void AddTelemetry(this IHostApplicationBuilder builder)
    {
        // The OTLP log record carries the trace context itself; as scope attributes it would be sent twice.
        builder.Logging.Configure(static logging =>
            logging.ActivityTrackingOptions = ActivityTrackingOptions.None
        );
        OpenTelemetryBuilder telemetry = builder
            .Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource
                    .AddService(ServiceName, serviceVersion: Version())
                    .AddAttributes([
                        new("deployment.environment.name", builder.Environment.EnvironmentName),
                        // Raw, so a bad role reaches the start validation, which fails with the real message.
                        new(
                            "frappe.role",
                            builder
                                .Configuration[$"{PlatformOptions.SectionName}:Role"]
                                ?.ToLowerInvariant() ?? "all"
                        ),
                    ])
                    // Last, so OTEL_SERVICE_NAME and OTEL_RESOURCE_ATTRIBUTES (a pod's instance id) win.
                    .AddEnvironmentVariableDetector()
            )
            .WithTracing(static tracing =>
                tracing
                    // Probes would fill the traces with one span per kubelet check.
                    .AddAspNetCoreInstrumentation(static options =>
                        options.Filter = static context =>
                            !context.Request.Path.StartsWithSegments(
                                "/health",
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
            )
            .WithMetrics(static metrics =>
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    // The runtime's built-in meter (GC, thread pool, exceptions) and Npgsql's connection pool and commands.
                    .AddMeter("System.Runtime", "Npgsql")
                    // A histogram or counter keeps a sample of the traces it measured, to jump from a spike to them.
                    .SetExemplarFilter(ExemplarFilterType.TraceBased)
            )
            .WithLogging(
                configureBuilder: null,
                configureOptions: static logging =>
                {
                    logging.IncludeScopes = true;
                    logging.IncludeFormattedMessage = true;
                }
            );

        // Without an endpoint (tests, a plain dotnet run) nothing is exported; compose and every deployment set it.
        // Only this generic variable switches export on: a per-signal endpoint alone exports nothing.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }
    }

    // The informational version carries the commit: the SDK appends +<SourceRevisionId>, which the image build sets.
    private static string? Version() =>
        Assembly
            .GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
}
