using System.Text.Json;
using Frappe.Platform.Health;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests;

public sealed class HealthTests(ApiFixture api)
{
    [Fact]
    public async Task Live_ChecksNoDependencies_ReturnsHealthy()
    {
        var result = await api.Host.Scenario(s =>
        {
            s.Get.Url("/health/live");
            s.StatusCodeShouldBe(200);
        });

        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        body.GetProperty("checks").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Ready_WithInfrastructureUp_ReportsEveryDependencyHealthy()
    {
        var result = await api.Host.Scenario(s =>
        {
            s.Get.Url("/health/ready");
            s.StatusCodeShouldBe(200);
        });

        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        var checks = body.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("criticality").GetString(), StringComparer.Ordinal);
        checks.ShouldBe(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["postgres"] = "critical",
            ["valkey"] = "non-critical",
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task Ready_CriticalDependencyDown_Returns503()
    {
        await using var host = await api.CreateHostAsync(services => services.AddHealthChecks()
            .AddDependency("broken-critical", DependencyCriticality.Critical, (_, _) => throw new InvalidOperationException("down")));

        var result = await host.Scenario(s =>
        {
            s.Get.Url("/health/ready");
            s.StatusCodeShouldBe(503);
        });

        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().ShouldBe("Unhealthy");

        // Probes are anonymous: each entry exposes exactly these fields, never exception details.
        var broken = body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "broken-critical");
        broken.EnumerateObject().Select(p => p.Name).ShouldBe(["name", "status", "criticality", "durationMs"], ignoreOrder: true);
    }

    [Fact]
    public async Task Ready_NonCriticalDependencyDown_StaysReadyButDegraded()
    {
        await using var host = await api.CreateHostAsync(services => services.AddHealthChecks()
            .AddDependency("broken-optional", DependencyCriticality.NonCritical, (_, _) => throw new InvalidOperationException("down")));

        var result = await host.Scenario(s =>
        {
            s.Get.Url("/health/ready");
            s.StatusCodeShouldBe(200);
        });

        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().ShouldBe("Degraded");
    }

    [Fact]
    public async Task Live_CriticalDependencyDown_StillHealthy()
    {
        await using var host = await api.CreateHostAsync(services => services.AddHealthChecks()
            .AddDependency("broken-critical", DependencyCriticality.Critical, (_, _) => throw new InvalidOperationException("down")));

        await host.Scenario(s =>
        {
            s.Get.Url("/health/live");
            s.StatusCodeShouldBe(200);
        });
    }
}
