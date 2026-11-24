using Frappe.Scheduling;
using Frappe.Testing.Containers;
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
    public async Task A_job_that_runs_on_startup_fires_with_a_cron_that_is_far_in_the_future()
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
                    quartz.AddCronJob<TickJob>(new JobKey("tick", "tests"), "Tests:Tick", "0 0 0 1 1 ? 2099", runOnStartup: true));
            using var host = builder.Build();

            // Act
            await host.StartAsync();

            try
            {
                await fired.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(r => r.Name == "scheduler");

                // Assert
                using (Assert.Multiple())
                {
                    await Assert.That(fired.Task.IsCompletedSuccessfully).IsTrue();
                    await Assert.That(report.Entries["scheduler"].Status).IsEqualTo(HealthStatus.Healthy);
                }
            }
            finally
            {
                await host.StopAsync();
            }
        });
    }

    [Test]
    public async Task Without_provisioning_the_scheduler_creates_no_tables_and_provisioning_creates_them()
    {
        await ScratchDatabase.RunAsync(server, [], async connectionString =>
        {
            // Arrange
            const string Table = "SELECT to_regclass('quartz.qrtz_job_details')::text";
            await SchedulingRegistration.EnsureSchemaAsync(connectionString, CancellationToken.None);
            await using var services = new ServiceCollection()
                .AddLogging()
                .AddFrappeScheduling(connectionString, _ => { }, provisionSchema: false)
                .BuildServiceProvider();

            // Act
            var factory = services.GetRequiredService<ISchedulerFactory>();
            var failure = await Assert.ThrowsAsync<Exception>(async () => await (await factory.GetScheduler()).GetJobGroupNames());
            var before = await ScratchDatabase.ScalarAsync(connectionString, Table);
            await SchedulingRegistration.ProvisionAsync(connectionString, CancellationToken.None);
            var after = await ScratchDatabase.ScalarAsync(connectionString, Table);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(failure).IsNotNull();
                await Assert.That(before is null or DBNull).IsTrue();
                await Assert.That(after).IsEqualTo("quartz.qrtz_job_details");
            }
        });
    }

    [Test]
    public async Task A_job_a_module_adds_through_its_services_is_in_the_scheduler_once_the_host_starts()
    {
        await ScratchDatabase.RunAsync(server, [], async connectionString =>
        {
            // Arrange
            await SchedulingRegistration.ProvisionAsync(connectionString, CancellationToken.None);
            var key = new JobKey("probe", "orders");
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Services.AddLogging()
                .AddSingleton(new TaskCompletionSource())
                .AddFrappeScheduling(connectionString, _ => { })
                .ConfigureAllQuartzSchedulers(quartz => quartz.AddJob<TickJob>(job => job.WithIdentity(key).StoreDurably()));
            using var host = builder.Build();

            // Act
            await host.StartAsync();

            try
            {
                var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();

                // Assert
                await Assert.That(await scheduler.GetJobDetail(key)).IsNotNull();
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
