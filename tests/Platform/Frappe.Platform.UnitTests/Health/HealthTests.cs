using System.Net;
using System.Net.Sockets;
using Frappe.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Health;

/// <summary>Critical and optional dependencies, the startup check and the TCP probes (S10).</summary>
// FreePort() releases the port it found before the test binds it, so two tests running at once can get the same one: every test
// class that opens real TCP ports runs alone among them.
[NotInParallel("tcp-ports")]
public sealed class HealthTests
{
    /// <summary>A dependency that answers or not; a failure reports the registration's failure status, as real checks do.</summary>
    private sealed class Switch(HealthStatus status) : IHealthCheck
    {
        public HealthStatus Status { get; set; } = status;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(Status == HealthStatus.Healthy
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, "secret-host:5432 refused"));
    }

    private static IHost Build(Switch critical, Switch optional)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Logging.ClearProviders();
        builder.Services
            .AddCriticalDependency("database", _ => critical)
            .AddCriticalDependency("database", _ => new Switch(HealthStatus.Healthy))
            .AddOptionalDependency("search", _ => optional);
        return builder.Build();
    }

    private static Task<HealthReport> ReadyAsync(IHost host) =>
        host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Tags.Contains(HealthProbes.Ready));

    [Test]
    public async Task Readiness_is_the_sum_of_the_critical_dependencies()
    {
        // Arrange
        var critical = new Switch(HealthStatus.Healthy);
        using var host = Build(critical, new Switch(HealthStatus.Healthy));
        var healthy = await ReadyAsync(host);
        critical.Status = HealthStatus.Unhealthy;

        // Act
        var down = await ReadyAsync(host);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(healthy.Status).IsEqualTo(HealthStatus.Healthy);
            await Assert.That(down.Status).IsEqualTo(HealthStatus.Unhealthy);
        }
    }

    [Test]
    public async Task An_optional_dependency_down_degrades_readiness_without_failing_it()
    {
        // Arrange
        using var host = Build(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Unhealthy));

        // Act
        var ready = await ReadyAsync(host);

        // Assert
        await Assert.That(ready.Status).IsEqualTo(HealthStatus.Degraded);
    }

    [Test]
    public async Task Liveness_never_checks_dependencies_so_an_outage_restarts_nothing()
    {
        // Arrange
        using var host = Build(new Switch(HealthStatus.Unhealthy), new Switch(HealthStatus.Unhealthy));

        // Act
        var live = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Tags.Contains(HealthProbes.Live));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(live.Status).IsEqualTo(HealthStatus.Healthy);
            await Assert.That(live.Entries.Keys).IsEquivalentTo(["process"]);
        }
    }

    [Test]
    public async Task A_dependency_registered_twice_is_checked_once_and_keeps_the_first_registration()
    {
        // Arrange
        using var host = Build(new Switch(HealthStatus.Unhealthy), new Switch(HealthStatus.Healthy));

        // Act
        var ready = await ReadyAsync(host);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(ready.Entries.Keys).IsEquivalentTo(["database", "search"]);
            await Assert.That(ready.Status).IsEqualTo(HealthStatus.Unhealthy);
        }
    }

    [Test]
    public async Task Startup_is_healthy_only_once_the_host_has_started()
    {
        // Arrange
        using var host = Build(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Healthy));
        var health = host.Services.GetRequiredService<HealthCheckService>();

        var before = await health.CheckHealthAsync(r => r.Tags.Contains(HealthProbes.Startup));
        await host.StartAsync();

        // Act
        var after = await health.CheckHealthAsync(r => r.Tags.Contains(HealthProbes.Startup));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(after.Status).IsEqualTo(HealthStatus.Healthy);
        }
    }

    [Test]
    public async Task Each_TCP_probe_port_is_open_only_while_its_probe_is_healthy()
    {
        // Arrange
        var ports = new TcpProbePorts(FreePort(), FreePort(), FreePort());
        await using var services = new ServiceCollection().AddLogging().AddTcpHealthProbes(ports).BuildServiceProvider();
        var publisher = services.GetServices<IHealthCheckPublisher>().Single();

        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);
        var whileHealthy = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");
        await publisher.PublishAsync(Report(HealthStatus.Unhealthy), CancellationToken.None);

        // Act
        var whileDown = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");
        var liveWhileDown = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Live}");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(whileHealthy).IsEqualTo(0);
            await Assert.That(whileDown).IsEqualTo(1);
            await Assert.That(liveWhileDown).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_port_that_could_not_be_opened_stays_closed_and_is_opened_by_the_next_publish()
    {
        // Arrange
        var ports = new TcpProbePorts(FreePort(), FreePort(), FreePort());
        await using var services = new ServiceCollection().AddLogging().AddTcpHealthProbes(ports).BuildServiceProvider();
        var publisher = services.GetServices<IHealthCheckPublisher>().Single();
        var occupant = new TcpListener(IPAddress.Any, ports.Ready);
        occupant.Start();
        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);
        occupant.Stop();
        var afterFailure = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");

        // Act
        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);
        var afterRetry = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(afterFailure).IsEqualTo(1);
            await Assert.That(afterRetry).IsEqualTo(0);
        }
    }

    [Test]
    public async Task The_probe_command_answers_with_the_exit_code_a_container_healthcheck_expects_for_a_tcp_target()
    {
        // Act
        var nothingListening = await ProbeCommand.RunAsync($"tcp://localhost:{FreePort()}");
        var notATarget = await ProbeCommand.RunAsync("ready");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(nothingListening).IsEqualTo(1);
            await Assert.That(notATarget).IsEqualTo(1);
            await Assert.That(ProbeCommand.IsProbe(["--probe", "tcp://localhost:1"], out _)).IsTrue();
            await Assert.That(ProbeCommand.IsProbe([], out _)).IsFalse();
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static HealthReport Report(HealthStatus ready) => new(
        new Dictionary<string, HealthReportEntry>
        {
            ["process"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null, [HealthProbes.Live]),
            ["started"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null, [HealthProbes.Startup]),
            ["database"] = new(ready, null, TimeSpan.Zero, null, null, [HealthProbes.Ready]),
        },
        TimeSpan.Zero);
}
