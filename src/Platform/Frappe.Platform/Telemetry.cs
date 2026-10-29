using System.Reflection;
using Frappe.Platform.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Frappe.Platform;

// The OpenTelemetry baseline every process shares: traces, metrics and logs over OTLP, sharing one trace id.
// Adapters add their own sources and meters (Wolverine's in Messaging); OTEL_* variables configure the export.
internal static class Telemetry
{
    public const string ServiceName = "frappe";

    public static void AddTelemetry(this IHostApplicationBuilder builder)
    {
        OpenTelemetryBuilder telemetry = builder
            .Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource
                    .AddService(ServiceName, serviceVersion: Version())
                    .AddAttributes([
                        new("deployment.environment.name", builder.Environment.EnvironmentName),
                    ])
                    .AddDetector(static services => new RoleDetector(
                        services.GetRequiredService<IOptions<PlatformOptions>>()
                    ))
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
                    // The runtime's built-in meter: GC, thread pool, exceptions.
                    .AddMeter("System.Runtime")
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

    private sealed class RoleDetector(IOptions<PlatformOptions> platform) : IResourceDetector
    {
        public Resource Detect()
        {
            try
            {
                return new([new("frappe.role", platform.Value.Role.ToString().ToLowerInvariant())]);
            }
            // Resolved while the host builds its logger, before the start validates the options: a role that does
            // not bind or validate is left out here, and the start then fails with the real message.
            catch (Exception exception)
                when (exception is InvalidOperationException or OptionsValidationException)
            {
                return Resource.Empty;
            }
        }
    }
}
