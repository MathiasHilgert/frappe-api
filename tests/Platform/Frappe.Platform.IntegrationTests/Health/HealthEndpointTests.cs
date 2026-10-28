using Alba;
using Frappe.Platform.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Health;

public sealed class HealthEndpointTests
{
    // A failing check reports its registration's failure status, as community checks do.
    private static readonly Func<IServiceProvider, IHealthCheck> _failing =
        static _ => new FailingCheck();

    [Fact]
    public async Task A_failing_critical_dependency_fails_readiness_and_startup_but_not_liveness()
    {
        await using IAlbaHost host = await StartAsync(static checks =>
            checks.AddDependency("database", Criticality.Critical, _failing)
        );

        await ShouldAnswerAsync(host, "/health/ready", 503, "Unhealthy");
        await ShouldAnswerAsync(host, "/health/startup", 503, "Unhealthy");
        await ShouldAnswerAsync(host, "/health/live", 200, "Healthy");
    }

    [Fact]
    public async Task A_failing_non_critical_dependency_is_not_run_by_any_probe()
    {
        await using IAlbaHost host = await StartAsync(static checks =>
            checks.AddDependency("cache", Criticality.NonCritical, _failing)
        );

        // Only critical dependencies gate readiness, so the non-critical one is not run there.
        await ShouldAnswerAsync(host, "/health/ready", 200, "Healthy");
        await ShouldAnswerAsync(host, "/health/startup", 200, "Healthy");
        await ShouldAnswerAsync(host, "/health/live", 200, "Healthy");
    }

    [Fact]
    public async Task A_failing_non_critical_dependency_tagged_for_startup_degrades_startup()
    {
        await using IAlbaHost host = await StartAsync(static checks =>
            checks.AddDependency(
                "cache",
                Criticality.NonCritical,
                _failing,
                tags: HealthTags.Startup
            )
        );

        await ShouldAnswerAsync(host, "/health/startup", 200, "Degraded");
    }

    [Fact]
    public async Task A_failing_startup_check_fails_only_startup()
    {
        await using IAlbaHost host = await StartAsync(static checks =>
            checks.AddCheck(
                "warm-up",
                static () => HealthCheckResult.Unhealthy(),
                [HealthTags.Startup]
            )
        );

        await ShouldAnswerAsync(host, "/health/startup", 503, "Unhealthy");
        await ShouldAnswerAsync(host, "/health/ready", 200, "Healthy");
        await ShouldAnswerAsync(host, "/health/live", 200, "Healthy");
    }

    [Fact]
    public async Task The_detailed_report_is_not_mapped_outside_development()
    {
        await using IAlbaHost host = await StartAsync(static _ => { }, Environments.Production);

        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/health");
            scenario.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task The_detailed_report_lists_every_check_without_exception_text_or_data()
    {
        await using IAlbaHost host = await StartAsync(static checks =>
            checks
                .AddDependency(
                    "database",
                    Criticality.Critical,
                    static _ => new ThrowingCheck("Host=db;Password=s3cret")
                )
                .AddDependency(
                    "cache",
                    Criticality.NonCritical,
                    Returning(
                        HealthCheckResult.Healthy(
                            "Cache answered.",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                ["token"] = "t0ken",
                            }
                        )
                    )
                )
        );

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/health");
            scenario.StatusCodeShouldBe(503);
        });
        string body = await result.ReadAsTextAsync();

        result.Context.Response.ContentType.ShouldBe("application/json; charset=utf-8");
        body.ShouldContain("\"status\":\"Unhealthy\"");
        body.ShouldContain("\"database\":{\"status\":\"Unhealthy\",\"duration\":");
        body.ShouldContain("\"description\":\"Cache answered.\"");
        body.ShouldContain("\"self\":");
        body.ShouldNotContain("s3cret");
        body.ShouldNotContain("t0ken");
    }

    private static Func<IServiceProvider, IHealthCheck> Returning(HealthCheckResult result) =>
        _ => new StubCheck(result);

    private static async Task<IAlbaHost> StartAsync(
        Action<IHealthChecksBuilder> addChecks,
        string environment = "Development"
    )
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment }
        );
        builder.AddHealth();
        addChecks(builder.Services.AddHealthChecks());
        return await AlbaHost.For(builder, static app => app.MapHealthEndpoints());
    }

    private static async Task ShouldAnswerAsync(
        IAlbaHost host,
        string url,
        int statusCode,
        string status
    )
    {
        await host.Scenario(scenario =>
        {
            scenario.Get.Url(url);
            scenario.StatusCodeShouldBe(statusCode);
            scenario.ContentShouldBe(status);
        });
    }

    private sealed class StubCheck(HealthCheckResult result) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(result);
    }

    private sealed class FailingCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus));
    }

    private sealed class ThrowingCheck(string message) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException(message);
    }
}
