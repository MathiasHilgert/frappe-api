using JasperFx.Resources;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Quartz;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>
/// Recurring jobs run on Quartz's clustered PostgreSQL store, so with several instances only one fires each trigger.
/// </summary>
public sealed class JobsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Scheduler_UsesTheClusteredPersistentStore()
    {
        var scheduler = await fixture.Host.Services.GetRequiredService<ISchedulerFactory>()
            .GetScheduler(TestContext.Current.CancellationToken);

        var metadata = await scheduler.GetMetadata(TestContext.Current.CancellationToken);

        metadata.JobStoreClustered.ShouldBeTrue();
        metadata.JobStorePersistent.ShouldBeTrue();
    }

    [Fact]
    public async Task Triggers_LiveInTheQuartzSchema_SoEveryInstanceSharesThem()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.PostgresConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT count(*) FROM quartz.qrtz_cron_triggers WHERE trigger_name = 'PurgePendingOwners'");

        var count = (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        count.ShouldBe(1);
    }

    [Fact]
    public async Task TwoInstances_ShareOneSchedulerCluster()
    {
        await using var second = await fixture.CreateHostAsync();
        await using var dataSource = NpgsqlDataSource.Create(fixture.PostgresConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(DISTINCT instance_name) FROM quartz.qrtz_scheduler_state");

        var instances = (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        instances.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task QuartzSchema_IsAResource_ForResourcesSetup()
    {
        var resource = fixture.Host.Services.GetServices<IStatefulResource>().Single(r => r.Name == "Quartz job store");

        await resource.Check(TestContext.Current.CancellationToken);
        await resource.Setup(TestContext.Current.CancellationToken); // idempotent on a provisioned database
    }
}
