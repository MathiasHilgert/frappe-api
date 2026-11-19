using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Frappe.Platform.IntegrationTests.Caching;

/// <summary>
/// The <c>valkey</c> readiness check against a real Valkey and an unreachable one. A test that expects a failure gives the
/// check time for its connection attempt, so the failure it sees is Valkey's own and not the check's timeout.
/// </summary>
[ClassDataSource<ValkeyServer>(Shared = SharedType.PerTestSession)]
public sealed class ValkeyHealthCheckTests(ValkeyServer server)
{
    private const string Check = "valkey";

    [Test]
    public async Task A_server_that_answers_is_healthy()
    {
        // Arrange
        await using var provider = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());

        // Act
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Name == Check);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(report.Entries[Check].Status).IsEqualTo(HealthStatus.Healthy);
            await Assert.That(report.Entries[Check].Tags).Contains("ready");
        }
    }

    [Test]
    public async Task An_unreachable_server_degrades_readiness_without_failing_it()
    {
        // Arrange
        await using var provider = CachingHost.Build(ValkeyServer.Unreachable, CachingHost.NewInstanceName(), services => Patient(services));

        // Act
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Name == Check);

        // Assert
        var entry = report.Entries[Check];
        using (Assert.Multiple())
        {
            await Assert.That(entry.Status).IsEqualTo(HealthStatus.Degraded);
            await Assert.That(entry.Description).IsEqualTo("Valkey does not answer.");
            await Assert.That(entry.Exception).IsTypeOf<RedisConnectionException>();
        }
    }

    [Test]
    public async Task An_unreachable_server_reports_the_failure_status_the_registration_carries()
    {
        // Arrange
        await using var provider = CachingHost.Build(ValkeyServer.Unreachable, CachingHost.NewInstanceName(), services => Patient(services, HealthStatus.Unhealthy));

        // Act
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Name == Check);

        // Assert
        var entry = report.Entries[Check];
        using (Assert.Multiple())
        {
            await Assert.That(entry.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(entry.Description).IsEqualTo("Valkey does not answer.");
        }
    }

    /// <summary>Gives the check time to give up on its connection, and optionally another failure status.</summary>
    private static void Patient(IServiceCollection services, HealthStatus? failureStatus = null) =>
        services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            var registration = options.Registrations.Single(r => r.Name == Check);
            registration.Timeout = TimeSpan.FromSeconds(30);
            registration.FailureStatus = failureStatus ?? registration.FailureStatus;
        });
}
