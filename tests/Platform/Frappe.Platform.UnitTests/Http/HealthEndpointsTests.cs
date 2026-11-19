using System.Net;
using System.Text.Json;
using Frappe.Health;
using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The Kubernetes probes over HTTP: <c>/health/live</c>, <c>/health/ready</c> and <c>/health/startup</c>.</summary>
public sealed class HealthEndpointsTests
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
            .AddFrappeHttp()
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

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Raw, string? CacheControl)> GetAsync(WebApplication app, string probe)
    {
        using var response = await app.GetTestClient().GetAsync($"/health/{probe}");
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        return (response.StatusCode, document.RootElement.Clone(), raw, response.Headers.CacheControl?.ToString());
    }

    private static string[] CheckNames(JsonElement body) =>
        [.. body.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).Order()];

    [Test]
    public async Task Readiness_is_the_sum_of_the_critical_dependencies()
    {
        // Arrange
        var critical = new Switch(HealthStatus.Healthy);
        await using var app = await StartAsync(critical, new Switch(HealthStatus.Healthy));

        var healthy = await GetAsync(app, "ready");
        critical.Status = HealthStatus.Unhealthy;

        // Act
        var down = await GetAsync(app, "ready");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(healthy.Status).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(healthy.Body.GetProperty("status").GetString()).IsEqualTo("Healthy");
            await Assert.That(down.Status).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(down.Body.GetProperty("status").GetString()).IsEqualTo("Unhealthy");
        }
    }

    [Test]
    public async Task An_optional_dependency_down_degrades_readiness_without_failing_it()
    {
        // Arrange
        await using var app = await StartAsync(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Unhealthy));

        // Act
        var ready = await GetAsync(app, "ready");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(ready.Status).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(ready.Body.GetProperty("status").GetString()).IsEqualTo("Degraded");
        }
    }

    [Test]
    public async Task Liveness_never_checks_dependencies_so_an_outage_restarts_nothing()
    {
        // Arrange
        await using var app = await StartAsync(new Switch(HealthStatus.Unhealthy), new Switch(HealthStatus.Unhealthy));

        // Act
        var live = await GetAsync(app, "live");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(live.Status).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(CheckNames(live.Body)).IsEquivalentTo(["process"]);
            await Assert.That(live.CacheControl).IsEqualTo("no-store");
        }
    }

    [Test]
    public async Task A_dependency_registered_twice_is_checked_once_and_the_answer_names_checks_without_internals()
    {
        // Arrange
        await using var app = await StartAsync(new Switch(HealthStatus.Unhealthy), new Switch(HealthStatus.Healthy));

        // Act
        var ready = await GetAsync(app, "ready");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(CheckNames(ready.Body)).IsEquivalentTo(["database", "search"]);
            await Assert.That(ready.Status).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(ready.Raw).DoesNotContain("secret-host");
            await Assert.That(ready.Body.GetProperty("checks")[0].TryGetProperty("durationMs", out _)).IsTrue();
        }
    }

    [Test]
    public async Task Probes_answer_every_time_and_need_no_language()
    {
        // Arrange
        await using var app = await StartAsync(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Healthy));
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await GetAsync(app, "live")).Status);
        }

        // Assert
        await Assert.That(statuses.Distinct().ToArray()).IsEquivalentTo([HttpStatusCode.OK]);
    }

    [Test]
    public async Task Startup_answers_503_until_the_host_has_started()
    {
        // Arrange
        await using var app = await StartAsync(new Switch(HealthStatus.Healthy), new Switch(HealthStatus.Healthy), start: false);
        var health = app.Services.GetRequiredService<HealthCheckService>();
        var before = await health.CheckHealthAsync(r => r.Tags.Contains(HealthProbes.Startup));
        await app.StartAsync();

        // Act
        var after = await GetAsync(app, "startup");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(after.Status).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(CheckNames(after.Body)).IsEquivalentTo(["started"]);
        }
    }
}
