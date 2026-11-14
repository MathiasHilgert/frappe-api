using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Frappe.Health;
using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Tests.Health;

/// <summary>Critical dependencies and the three probes (S23).</summary>
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

    private static async Task<WebApplication> StartAsync(Switch critical, Switch optional, bool start = true)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddFrappeHttp(new AnonymousRateLimit(1, TimeSpan.FromMinutes(10)))
            .AddCriticalDependency("database", _ => critical)
            .AddCriticalDependency("database", _ => new Switch(HealthStatus.Healthy))
            .AddOptionalDependency("search", _ => optional);

        var app = builder.Build();
        app.UseFrappeHttp();
        app.MapFrappeHealth();
        if (start)
        {
            await app.StartAsync();
        }

        return app;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> GetAsync(WebApplication app, string probe)
    {
        using var response = await app.GetTestClient().GetAsync($"/health/{probe}");
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        return (response.StatusCode, document.RootElement.Clone(), raw);
    }

    private static string[] CheckNames(JsonElement body) =>
        body.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).Order().ToArray();

    [Test]
    public async Task Readiness_is_the_sum_of_the_critical_dependencies()
    {
        var critical = new Switch(HealthStatus.Healthy);
        await using var app = await StartAsync(critical, new Switch(HealthStatus.Healthy));

        var healthy = await GetAsync(app, "ready");
        critical.Status = HealthStatus.Unhealthy;
        var down = await GetAsync(app, "ready");

        await Assert.That(healthy.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(healthy.Body.GetProperty("status").GetString()).IsEqualTo("Healthy");
        await Assert.That(down.Status).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(down.Body.GetProperty("status").GetString()).IsEqualTo("Unhealthy");
    }

    [Test]
    public async Task An_optional_dependency_down_degrades_readiness_without_failing_it()
    {
        await using var app = await StartAsync(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Unhealthy));

        var ready = await GetAsync(app, "ready");

        await Assert.That(ready.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(ready.Body.GetProperty("status").GetString()).IsEqualTo("Degraded");
    }

    [Test]
    public async Task Liveness_never_checks_dependencies_so_an_outage_restarts_nothing()
    {
        await using var app = await StartAsync(new Switch(HealthStatus.Unhealthy), new Switch(HealthStatus.Unhealthy));

        var live = await GetAsync(app, "live");

        await Assert.That(live.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(CheckNames(live.Body)).IsEquivalentTo(["process"]);
    }

    [Test]
    public async Task A_dependency_registered_twice_is_checked_once_and_the_answer_names_checks_without_internals()
    {
        await using var app = await StartAsync(new Switch(HealthStatus.Unhealthy), new Switch(HealthStatus.Healthy));

        var ready = await GetAsync(app, "ready");

        await Assert.That(CheckNames(ready.Body)).IsEquivalentTo(["database", "search"]);
        await Assert.That(ready.Status).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(ready.Raw).DoesNotContain("secret-host");
        await Assert.That(ready.Body.GetProperty("checks")[0].TryGetProperty("durationMs", out _)).IsTrue();
    }

    [Test]
    public async Task Probes_are_never_rate_limited_and_need_no_language()
    {
        await using var app = await StartAsync(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Healthy));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await GetAsync(app, "live")).Status);
        }

        await Assert.That(statuses.Distinct().ToArray()).IsEquivalentTo([HttpStatusCode.OK]);
    }

    [Test]
    public async Task Startup_is_healthy_only_once_the_host_has_started()
    {
        await using var app = await StartAsync(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Healthy), start: false);
        var health = app.Services.GetRequiredService<HealthCheckService>();

        var before = await health.CheckHealthAsync(r => r.Tags.Contains(HealthProbes.Startup));
        await app.StartAsync();
        var after = await health.CheckHealthAsync(r => r.Tags.Contains(HealthProbes.Startup));

        await Assert.That(before.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(after.Status).IsEqualTo(HealthStatus.Healthy);
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
        var ports = new TcpProbePorts(FreePort(), FreePort(), FreePort());
        await using var services = new ServiceCollection().AddLogging().AddTcpHealthProbes(ports).BuildServiceProvider();
        var publisher = services.GetServices<IHealthCheckPublisher>().Single();

        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);
        var whileHealthy = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");
        await publisher.PublishAsync(Report(HealthStatus.Unhealthy), CancellationToken.None);
        var whileDown = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Ready}");
        var liveWhileDown = await ProbeCommand.RunAsync($"tcp://localhost:{ports.Live}");

        await Assert.That(whileHealthy).IsEqualTo(0);
        await Assert.That(whileDown).IsEqualTo(1);
        await Assert.That(liveWhileDown).IsEqualTo(0);
    }

    [Test]
    public async Task The_probe_command_answers_with_the_exit_code_a_container_healthcheck_expects()
    {
        var critical = new Switch(HealthStatus.Healthy);
        var app = WebApplication.CreateSlimBuilder();
        app.WebHost.UseUrls("http://127.0.0.1:0");
        app.Logging.ClearProviders();
        app.Services.AddCriticalDependency("database", _ => critical);
        await using var web = app.Build();
        web.MapFrappeHealth();
        await web.StartAsync();
        var url = web.Urls.Single();

        var healthy = await ProbeCommand.RunAsync($"{url}/health/ready");
        critical.Status = HealthStatus.Unhealthy;
        var unhealthy = await ProbeCommand.RunAsync($"{url}/health/ready");
        var nothingListening = await ProbeCommand.RunAsync($"tcp://localhost:{FreePort()}");
        var notATarget = await ProbeCommand.RunAsync("ready");

        await Assert.That(healthy).IsEqualTo(0);
        await Assert.That(unhealthy).IsEqualTo(1);
        await Assert.That(nothingListening).IsEqualTo(1);
        await Assert.That(notATarget).IsEqualTo(1);
        await Assert.That(ProbeCommand.IsProbe(["--probe", "tcp://localhost:1"], out _)).IsTrue();
        await Assert.That(ProbeCommand.IsProbe([], out _)).IsFalse();
    }
}
