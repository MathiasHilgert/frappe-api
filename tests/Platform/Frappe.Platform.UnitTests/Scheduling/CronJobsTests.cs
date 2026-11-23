using Frappe.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Frappe.Platform.UnitTests.Scheduling;

/// <summary>A cron job's trigger is evaluated in UTC, whatever the host's time zone.</summary>
public sealed class CronJobsTests
{
    [Test]
    public async Task The_scheduled_trigger_runs_in_utc()
    {
        // Arrange
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddQuartz(quartz => quartz.AddCronJob<NoopJob>(new JobKey("noop", "tests"), "Tests:Noop", "0 0 3 * * ?"))
            .BuildServiceProvider();

        // Act
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        var scheduled = await scheduler.GetTrigger(new TriggerKey("noop-scheduled", "tests")) as ICronTrigger;
        await scheduler.Shutdown();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(scheduled).IsNotNull();
            await Assert.That(scheduled!.TimeZone).IsEqualTo(TimeZoneInfo.Utc);
        }
    }

    /// <summary>A job that does nothing.</summary>
    public sealed class NoopJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
