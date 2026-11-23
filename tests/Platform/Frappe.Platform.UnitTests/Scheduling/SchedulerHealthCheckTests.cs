using Frappe.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace Frappe.Platform.UnitTests.Scheduling;

/// <summary>The scheduler's readiness check is healthy only while the scheduler is started and neither in standby nor shut down.</summary>
public sealed class SchedulerHealthCheckTests
{
    private static async Task<HealthStatus> CheckAsync(IScheduler scheduler) => (await CheckResultAsync(scheduler)).Status;

    private static async Task<HealthCheckResult> CheckResultAsync(IScheduler scheduler)
    {
        var check = new SchedulerHealthCheck(new SingleSchedulerFactory(scheduler));
        var registration = new HealthCheckRegistration("scheduler", check, HealthStatus.Unhealthy, null);
        return await check.CheckHealthAsync(new HealthCheckContext { Registration = registration });
    }

    private static async Task<IScheduler> NewSchedulerAsync() =>
        await new ServiceCollection().AddQuartz().BuildServiceProvider().GetRequiredService<ISchedulerFactory>().GetScheduler();

    [Test]
    public async Task A_scheduler_not_started_yet_is_unhealthy()
    {
        // Arrange
        var scheduler = await NewSchedulerAsync();

        // Act
        var status = await CheckAsync(scheduler);
        await scheduler.Shutdown();

        // Assert
        await Assert.That(status).IsEqualTo(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task A_running_scheduler_is_healthy()
    {
        // Arrange
        var scheduler = await NewSchedulerAsync();
        await scheduler.Start();

        // Act
        var status = await CheckAsync(scheduler);
        await scheduler.Shutdown();

        // Assert
        await Assert.That(status).IsEqualTo(HealthStatus.Healthy);
    }

    [Test]
    public async Task A_scheduler_in_standby_is_unhealthy()
    {
        // Arrange
        var scheduler = await NewSchedulerAsync();
        await scheduler.Start();
        await scheduler.Standby();

        // Act
        var status = await CheckAsync(scheduler);
        await scheduler.Shutdown();

        // Assert
        await Assert.That(status).IsEqualTo(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task A_scheduler_shut_down_is_unhealthy()
    {
        // Arrange
        var scheduler = await NewSchedulerAsync();
        await scheduler.Start();
        await scheduler.Shutdown();

        // Act
        var status = await CheckAsync(scheduler);

        // Assert
        await Assert.That(status).IsEqualTo(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task An_unhealthy_result_names_the_scheduler_state()
    {
        // Arrange
        var scheduler = await NewSchedulerAsync();
        await scheduler.Start();
        await scheduler.Standby();

        // Act
        var result = await CheckResultAsync(scheduler);
        await scheduler.Shutdown();

        // Assert
        await Assert.That(result.Description).IsEqualTo("The scheduler is Standby, not running.");
        await Assert.That(result.Data["status"]).IsEqualTo("Standby");
    }

    /// <summary>A factory that hands out one scheduler.</summary>
    private sealed class SingleSchedulerFactory(IScheduler scheduler) : ISchedulerFactory
    {
        public ValueTask<List<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<List<IScheduler>>([scheduler]);

        public ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default) => ValueTask.FromResult(scheduler);

        public ValueTask<IScheduler?> LookupScheduler(string schedulerName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IScheduler?>(scheduler);
    }
}
