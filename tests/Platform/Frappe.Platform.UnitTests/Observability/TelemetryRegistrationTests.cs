using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Observability.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>What a host exports once AddFrappeTelemetry is called, without any module registering anything.</summary>
public sealed class TelemetryRegistrationTests
{
    private static IHost Host(Dictionary<string, string?> configuration, List<Activity> spans, List<Metric> metrics)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddFrappeTelemetry(
            "frappe-test",
            traces => traces.AddInMemoryExporter(spans),
            meters => meters.AddInMemoryExporter(metrics));
        return builder.Build();
    }

    [Test]
    public async Task Every_Frappe_source_and_meter_is_exported_without_registering_it()
    {
        // Arrange
        List<Activity> spans = [];
        List<Metric> metrics = [];
        using var host = Host([], spans, metrics);
        await host.StartAsync();

        // Act
        using (var source = new ActivitySource("Frappe.AnyModule"))
        {
            source.StartActivity("module work")?.Dispose();
        }

        using var meter = host.Services.GetRequiredService<IMeterFactory>().Create("Frappe.AnyModule");
        meter.CreateCounter<long>("frappe.any_module.things").Add(3);
        host.Services.GetRequiredService<MeterProvider>().ForceFlush();
        await host.StopAsync();
        // Stops the provider shipping spans, so spans of tests running alongside cannot change the list being read.
        host.Dispose();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(spans.Select(s => s.DisplayName)).Contains("module work");
            await Assert.That(metrics.Select(m => m.Name)).Contains("frappe.any_module.things");
        }
    }

    [Test]
    public async Task Sources_outside_Frappe_are_not_exported_unless_a_host_adds_them()
    {
        // Arrange
        List<Activity> spans = [];
        using var host = Host([], spans, []);
        await host.StartAsync();

        // Act
        using (var source = new ActivitySource("SomeLibrary"))
        {
            source.StartActivity("library work")?.Dispose();
        }

        await host.StopAsync();
        // Stops the provider shipping spans, so spans of tests running alongside cannot change the list being read.
        host.Dispose();

        // Assert
        // Frappe's own sources are process-wide, so spans of tests running alongside may reach this provider too.
        await Assert.That(spans.Where(s => s.Source.Name == "SomeLibrary")).IsEmpty();
    }

    [Test]
    [Arguments(null, "frappe-test")]
    [Arguments("frappe-api-blue", "frappe-api-blue")]
    public async Task The_service_name_is_the_host_default_unless_OTEL_SERVICE_NAME_says_otherwise(string? configured, string expected)
    {
        // Arrange
        List<Activity> spans = [];
        using var host = Host(new() { ["OTEL_SERVICE_NAME"] = configured }, spans, []);
        await host.StartAsync();

        var resource = host.Services.GetRequiredService<TracerProvider>().GetResource();

        // Act
        await host.StopAsync();

        // Assert
        await Assert.That(resource.Attributes.Single(a => a.Key == "service.name").Value).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null, "", false)]
    [Arguments("OTEL_EXPORTER_OTLP_ENDPOINT", "http://collector.invalid:4317", true)]
    [Arguments("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "http://collector.invalid:4317", true)]
    [Arguments("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", "http://collector.invalid:4317", true)]
    [Arguments("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT", "http://collector.invalid:4317", true)]
    [Arguments("OTEL_EXPORTER_OTLP_ENDPOINT", "   ", false)]
    public async Task The_OTLP_exporter_is_added_only_when_an_endpoint_is_configured(string? key, string value, bool expected)
    {
        // Arrange
        var configuration = new Dictionary<string, string?>();
        if (key is not null)
        {
            configuration[key] = value;
        }

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddFrappeTelemetry("frappe-test");

        // Act
        // The exporter types are internal to OpenTelemetry, so the registered descriptors are probed by type name.
        var registered = builder.Services.Any(d => d.ServiceType.FullName?.Contains("Otlp", StringComparison.Ordinal) == true
            || d.ImplementationType?.FullName?.Contains("Otlp", StringComparison.Ordinal) == true);

        // Assert
        await Assert.That(registered).IsEqualTo(expected);
    }

    [Test]
    public async Task A_span_follows_the_sampling_decision_of_its_parent()
    {
        // Arrange
        List<Activity> spans = [];
        using var host = Host([], spans, []);
        await host.StartAsync();

        // Act
        var traceId = ActivityTraceId.CreateRandom();
        var unsampled = new ActivityContext(traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.None, isRemote: true);
        var sampled = new ActivityContext(traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: true);
        using (var source = new ActivitySource("Frappe.SamplerProbe.Child"))
        {
            source.StartActivity("unsampled parent", ActivityKind.Internal, unsampled)?.Dispose();
            source.StartActivity("sampled parent", ActivityKind.Internal, sampled)?.Dispose();
        }

        await host.StopAsync();
        // Stops the provider shipping spans, so spans of tests running alongside cannot change the list being read.
        host.Dispose();

        // Assert
        await Assert.That(spans.Select(s => s.DisplayName)).Contains("sampled parent").And.DoesNotContain("unsampled parent");
    }
}
