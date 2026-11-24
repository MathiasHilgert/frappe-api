using Frappe.Health;
using Frappe.Modules;
using Frappe.Persistence;
using Frappe.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using Quartz;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The Worker as it is composed for deployment (not Development), built and validated as at startup: what the platform
/// gives it with no module, and what a module contributes through its services (CFG-07). Nothing is started, so no database,
/// NATS or Valkey is reached.
/// </summary>
public sealed class WorkerCompositionTests
{
    private static readonly Dictionary<string, string?> Deployed = new()
    {
        ["ConnectionStrings:Scheduler"] = "Host=unreachable.invalid;Database=frappe;Username=frappe_scheduler",
        ["ConnectionStrings:Valkey"] = "unreachable.invalid:6379",
        ["ConnectionStrings:Nats"] = "nats://unreachable.invalid:4222",
        ["ConnectionStrings:Notifications"] = "Host=unreachable.invalid;Database=frappe;Username=frappe_notifications_worker",
    };

    private static IHost Build(Dictionary<string, string?> settings, IReadOnlyCollection<IModule>? modules = null,
        Action<IServiceCollection>? more = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(settings);
        more?.Invoke(builder.Services);
        _ = modules is null ? builder.AddFrappeWorker() : builder.AddFrappeWorker(modules);
        return builder.Build();
    }

    private static Task ValidateAsync(IHost host) =>
        host.Services.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None);

    [Test]
    public async Task The_Worker_starts_with_zero_modules_and_runs_the_scheduler_and_the_tcp_probes()
    {
        // Arrange
        using var host = Build(Deployed, []);

        // Act
        await ValidateAsync(host);
        var hosted = host.Services.GetServices<IHostedService>().Select(service => service.GetType().Name).ToArray();
        var publishers = host.Services.GetServices<IHealthCheckPublisher>().Select(publisher => publisher.GetType().Name).ToArray();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(hosted).Contains("QuartzHostedService");
            await Assert.That(publishers).Contains("TcpProbePublisher");
        }
    }

    [Test]
    public async Task Readiness_counts_the_scheduler_and_NATS_as_critical_and_Valkey_as_optional()
    {
        // Arrange
        using var host = Build(Deployed, []);

        // Act
        var registrations = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Where(registration => registration.Tags.Contains(HealthProbes.Ready))
            .ToDictionary(registration => registration.Name, registration => registration.FailureStatus);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(registrations["scheduler-database"]).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(registrations["scheduler"]).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(registrations["nats"]).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(registrations["valkey"]).IsEqualTo(HealthStatus.Degraded);
        }
    }

    [Test]
    public async Task The_scheduler_job_store_is_a_database_participant_the_Migrator_provisions()
    {
        // Arrange
        using var host = Build(Deployed, []);

        // Act
        var participant = host.Services.GetServices<IDatabaseParticipant>().Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(participant.Schema).IsEqualTo("quartz");
            await Assert.That(participant.Roles.Single().Name).IsEqualTo("frappe_scheduler");
        }
    }

    [Test]
    public async Task A_missing_scheduler_connection_string_fails_the_start_naming_its_key()
    {
        // Arrange
        var settings = new Dictionary<string, string?>(Deployed);
        settings.Remove("ConnectionStrings:Scheduler");
        using var host = Build(settings, []);

        // Act
        var thrown = await Assert.ThrowsAsync<AggregateException>(() => ValidateAsync(host));

        // Assert
        await Assert.That(thrown?.InnerExceptions.OfType<OptionsValidationException>().Select(failure => failure.Message))
            .Contains(message => message.Contains("ConnectionStrings:Scheduler", StringComparison.Ordinal));
    }

    [Test]
    public async Task The_Worker_with_its_real_module_lists_validates_and_runs_the_relay_and_the_notifications_database()
    {
        // Arrange
        using var host = Build(Deployed);

        // Act
        await ValidateAsync(host);
        var hosted = host.Services.GetServices<IHostedService>().Select(service => service.GetType().Name).ToArray();
        var ready = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Where(registration => registration.Tags.Contains(HealthProbes.Ready))
            .Select(registration => registration.Name)
            .ToArray();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(Frappe.Worker.Modules.All.Select(module => module.GetType().Name))
                .IsEquivalentTo(["NotificationsModule"]);
            await Assert.That(hosted).Contains("OutboxRelayService`1");
            await Assert.That(ready).Contains("notifications-database");
        }
    }

    [Test]
    public async Task The_Worker_with_its_real_module_lists_and_no_notifications_connection_fails_the_start_naming_its_key()
    {
        // Arrange
        var settings = new Dictionary<string, string?>(Deployed);
        settings.Remove("ConnectionStrings:Notifications");
        using var host = Build(settings);

        // Act
        var thrown = await Assert.ThrowsAsync<OptionsValidationException>(() => ValidateAsync(host));

        // Assert
        await Assert.That(thrown?.Message).Contains("ConnectionStrings:Notifications");
    }

    [Test]
    public async Task A_module_contributes_a_job_and_a_meter_view_through_its_services()
    {
        // Arrange
        var exported = new List<Metric>();
        using var host = Build(Deployed, [new ProbeModule()],
            services => services.ConfigureOpenTelemetryMeterProvider(meters => meters.AddInMemoryExporter(exported)));

        // Act
        await ValidateAsync(host);
        var job = host.Services.GetService<ProbeJob>();
        var provider = host.Services.GetRequiredService<MeterProvider>();
        ProbeModule.Latency.Record(0.5);
        provider.ForceFlush();
        var points = exported.Single(metric => metric.Name == ProbeModule.LatencyMetric).GetMetricPoints().GetEnumerator();
        points.MoveNext();
        var bounds = new List<double>();
        foreach (var bucket in points.Current.GetHistogramBuckets())
        {
            bounds.Add(bucket.ExplicitBound);
        }


        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(job).IsNotNull();
            await Assert.That(bounds.Where(bound => !double.IsPositiveInfinity(bound))).IsEquivalentTo(ProbeModule.Boundaries);
        }
    }

    /// <summary>A test-only module with one job and one meter view, standing for what a real module contributes.</summary>
    private sealed class ProbeModule : IModule
    {
        public const string LatencyMetric = "orders.probe.latency";

        public static readonly double[] Boundaries = [1, 2, 3];

        public static readonly System.Diagnostics.Metrics.Histogram<double> Latency =
            new System.Diagnostics.Metrics.Meter("Frappe.Orders").CreateHistogram<double>(LatencyMetric);

        public void AddServices(IServiceCollection services)
        {
            services.ConfigureAllQuartzSchedulers(quartz => quartz.AddJob<ProbeJob>(job => job.WithIdentity("probe", "orders").StoreDurably()));
            services.ConfigureOpenTelemetryMeterProvider(meters => meters.AddView(LatencyMetric,
                new ExplicitBucketHistogramConfiguration { Boundaries = Boundaries }));
        }
    }

    /// <summary>The job <see cref="ProbeModule"/> registers.</summary>
    private sealed class ProbeJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
