using Frappe.Platform.IntegrationTests.Persistence;
using Frappe.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Quartz;

namespace Frappe.Platform.IntegrationTests.Scheduling;

/// <summary>The clustered scheduler against a real Postgres: a cron job fires, and the readiness check follows the database.</summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class SchedulerHostTests(PostgresServer server)
{
    private const string Check = "scheduler-database";

    [Test]
    public async Task A_cron_job_fires_on_its_schedule_and_the_database_check_is_healthy()
    {
        await ScratchDatabase.RunAsync(server, [], async connectionString =>
        {
            // Arrange
            await SchedulingRegistration.ProvisionAsync(connectionString, CancellationToken.None);
            var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Services.AddLogging()
                .AddSingleton(fired)
                .AddFrappeScheduling(connectionString, quartz =>
                    quartz.AddCronJob<TickJob>(new JobKey("tick", "tests"), "Tests:Tick", "* * * * * ?", runOnStartup: false));
            using var host = builder.Build();
            var provider = host.Services;

            // Act
            await host.StartAsync();

            try
            {
                await fired.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Name == Check);

                // Assert
                using (Assert.Multiple())
                {
                    await Assert.That(report.Entries[Check].Status).IsEqualTo(HealthStatus.Healthy);
                }
            }
            finally
            {
                await host.StopAsync();
            }
        });
    }

    [Test]
    public async Task An_unreachable_database_makes_the_check_unhealthy()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddFrappeScheduling("Host=127.0.0.1;Port=1;Username=x;Password=x;Database=x;Timeout=2", _ => { })
            .BuildServiceProvider();

        // Act
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Name == Check);

        // Assert
        await Assert.That(report.Entries[Check].Status).IsEqualTo(HealthStatus.Unhealthy);
    }

    /// <summary>Completes the signal the test waits on.</summary>
    public sealed class TickJob(TaskCompletionSource fired) : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            fired.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
