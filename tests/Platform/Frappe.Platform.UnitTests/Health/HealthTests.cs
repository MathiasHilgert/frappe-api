using System.Net;
using System.Net.Sockets;
using Frappe.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Health;

/// <summary>Critical dependencies and the three probes.</summary>
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

    private static async Task<IHost> StartAsync(Switch critical, Switch optional, bool start = true)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services
            .AddCriticalDependency("database", _ => critical)
            .AddCriticalDependency("database", _ => new Switch(HealthStatus.Healthy))
            .AddOptionalDependency("search", _ => optional);

        var host = builder.Build();
        if (start)
        {
            await host.StartAsync();
        }

        return host;
    }

    private static Task<HealthReport> CheckAsync(IHost host, string probe) =>
        host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Tags.Contains(probe));

    [Test]
    public async Task Readiness_is_the_sum_of_the_critical_dependencies()
    {
        // Arrange
        var critical = new Switch(HealthStatus.Healthy);
        using var host = await StartAsync(critical, new Switch(HealthStatus.Healthy));

        var healthy = await CheckAsync(host, HealthProbes.Ready);
        critical.Status = HealthStatus.Unhealthy;

        // Act
        var down = await CheckAsync(host, HealthProbes.Ready);

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
        using var host = await StartAsync(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Unhealthy));

        // Act
        var ready = await CheckAsync(host, HealthProbes.Ready);

        // Assert
        await Assert.That(ready.Status).IsEqualTo(HealthStatus.Degraded);
    }

    [Test]
    public async Task Liveness_never_checks_dependencies_so_an_outage_restarts_nothing()
    {
        // Arrange
        using var host = await StartAsync(new Switch(HealthStatus.Unhealthy), new Switch(HealthStatus.Unhealthy));

        // Act
        var live = await CheckAsync(host, HealthProbes.Live);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(live.Status).IsEqualTo(HealthStatus.Healthy);
            await Assert.That(live.Entries.Keys.ToArray()).IsEquivalentTo(["process"]);
        }
    }

    [Test]
    public async Task A_dependency_registered_twice_is_checked_once()
    {
        // Arrange
        using var host = await StartAsync(new Switch(HealthStatus.Unhealthy), new Switch(HealthStatus.Healthy));

        // Act
        var ready = await CheckAsync(host, HealthProbes.Ready);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(ready.Entries.Keys.Order().ToArray()).IsEquivalentTo(["database", "search"]);
            await Assert.That(ready.Status).IsEqualTo(HealthStatus.Unhealthy);
        }
    }

    [Test]
    public async Task Startup_is_healthy_only_once_the_host_has_started()
    {
        // Arrange
        using var host = await StartAsync(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Healthy), start: false);
        var before = await CheckAsync(host, HealthProbes.Startup);
        await host.StartAsync();

        // Act
        var after = await CheckAsync(host, HealthProbes.Startup);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(after.Status).IsEqualTo(HealthStatus.Healthy);
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
        var startupWhileHealthy = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Startup}");
        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);
        var reopened = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(whileHealthy).IsEqualTo(0);
            await Assert.That(whileDown).IsEqualTo(1);
            await Assert.That(liveWhileDown).IsEqualTo(0);
            await Assert.That(startupWhileHealthy).IsEqualTo(0);
            await Assert.That(reopened).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Dependency_registrations_carry_the_check_timeout()
    {
        // Arrange
        await using var services = new ServiceCollection()
            .AddCriticalDependency("database", _ => new Switch(HealthStatus.Healthy))
            .BuildServiceProvider();

        // Act
        var registration = services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single(r => r.Name == "database");

        // Assert
        await Assert.That(registration.Timeout).IsEqualTo(HealthRegistration.CheckTimeout);
    }

    [Test]
    public async Task Disposing_the_publisher_closes_every_probe_port()
    {
        // Arrange
        var ports = new TcpProbePorts(FreePort(), FreePort(), FreePort());
        var services = new ServiceCollection().AddLogging().AddTcpHealthProbes(ports).BuildServiceProvider();
        await services.GetServices<IHealthCheckPublisher>().Single().PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);
        var whileRunning = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");

        // Act
        await services.DisposeAsync();
        var afterDispose = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(whileRunning).IsEqualTo(0);
            await Assert.That(afterDispose).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_port_that_could_not_be_opened_is_retried_on_the_next_report()
    {
        // Arrange
        var ports = new TcpProbePorts(FreePort(), FreePort(), FreePort());
        await using var services = new ServiceCollection().AddLogging().AddTcpHealthProbes(ports).BuildServiceProvider();
        var publisher = services.GetServices<IHealthCheckPublisher>().Single();
        var occupier = new TcpListener(IPAddress.Any, ports.Ready);
        occupier.Start();
        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);
        occupier.Stop();

        // Act
        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);
        var reopened = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");

        // Assert
        await Assert.That(reopened).IsEqualTo(0);
    }

    [Test]
    public async Task The_probe_command_answers_with_the_exit_code_a_container_healthcheck_expects()
    {
        // Arrange
        var port = FreePort();
        var status = HttpStatusCode.OK;
        using var server = new HttpListener();
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        var serving = Task.Run(async () =>
        {
            while (server.IsListening)
            {
                try
                {
                    var context = await server.GetContextAsync();
                    context.Response.StatusCode = (int)status;
                    context.Response.Close();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }
            }
        });

        var healthy = await ProbeCommand.RunAsync($"http://127.0.0.1:{port}/health/ready");
        status = HttpStatusCode.ServiceUnavailable;

        // Act
        var unhealthy = await ProbeCommand.RunAsync($"http://127.0.0.1:{port}/health/ready");
        var nothingListening = await ProbeCommand.RunAsync($"tcp://localhost:{FreePort()}");
        var notATarget = await ProbeCommand.RunAsync("ready");
        server.Stop();
        await serving;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(healthy).IsEqualTo(0);
            await Assert.That(unhealthy).IsEqualTo(1);
            await Assert.That(nothingListening).IsEqualTo(1);
            await Assert.That(notATarget).IsEqualTo(1);
            await Assert.That(ProbeCommand.IsProbe(["--probe", "tcp://localhost:1"], out _)).IsTrue();
            await Assert.That(ProbeCommand.IsProbe([], out _)).IsFalse();
        }
    }

    [Test]
    [Arguments("tcp://localhost")]
    [Arguments("ftp://localhost:8082/ready")]
    [Arguments("file:///health/ready")]
    public async Task A_malformed_probe_target_exits_1_and_says_why(string target)
    {
        // Arrange
        await using var error = new StringWriter();

        // Act
        var exitCode = await ProbeCommand.RunAsync(target, error);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(1);
            await Assert.That(error.ToString().Trim()).IsEqualTo($"'{target}' is not a probe target (http://host:port/path or tcp://host:port).");
        }
    }
}
