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
    public async Task ARestart_KeepsTheTriggersState_WhenTheScheduleIsUnchanged()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.PostgresConnectionString);
        var original = await ScalarAsync<long>(dataSource, "SELECT next_fire_time FROM quartz.qrtz_triggers WHERE trigger_name = 'PurgePendingOwners'");
        // 2100-01-01 in the store's ticks: a state only the store knows, far enough ahead never to misfire.
        var kept = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        await ScalarAsync<int>(dataSource, $"UPDATE quartz.qrtz_triggers SET next_fire_time = {kept} WHERE trigger_name = 'PurgePendingOwners' RETURNING 1");
        try
        {
            await (await fixture.CreateHostAsync()).DisposeAsync(); // a restart

            (await ScalarAsync<long>(dataSource, "SELECT next_fire_time FROM quartz.qrtz_triggers WHERE trigger_name = 'PurgePendingOwners'"))
                .ShouldBe(kept);
        }
        finally
        {
            await ScalarAsync<int>(dataSource, $"UPDATE quartz.qrtz_triggers SET next_fire_time = {original} WHERE trigger_name = 'PurgePendingOwners' RETURNING 1");
        }
    }

    [Fact]
    public async Task ARestart_AppliesAChangedSchedule()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.PostgresConnectionString);
        await ScalarAsync<int>(dataSource,
            "UPDATE quartz.qrtz_cron_triggers SET cron_expression = '0 0 4 * * ?' WHERE trigger_name = 'PurgePendingOwners' RETURNING 1");

        await (await fixture.CreateHostAsync()).DisposeAsync(); // a restart

        (await ScalarAsync<string>(dataSource, "SELECT cron_expression FROM quartz.qrtz_cron_triggers WHERE trigger_name = 'PurgePendingOwners'"))
            .ShouldBe("0 0 3 * * ?");
    }

    [Fact]
    public async Task ARestart_DeletesRecurringJobsNoModuleDeclaresAnyMore_AndKeepsTheDeclaredOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        var scheduler = await fixture.Host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(ct);
        var stale = new JobKey($"Stale-{Guid.NewGuid():N}");
        await scheduler.AddJob(JobBuilder.Create<Frappe.Platform.Messaging.Jobs.PublishCommandJob<StaleCommand>>()
            .WithIdentity(stale).StoreDurably().Build(), AddJobOptions.Replacing, ct);

        await (await fixture.CreateHostAsync()).DisposeAsync(); // a restart

        (await scheduler.GetJobDetail(stale, ct)).ShouldBeNull();
        (await scheduler.GetJobDetail(new JobKey("PurgePendingOwners"), ct)).ShouldNotBeNull();
    }

    public sealed record StaleCommand;

    private static async Task<T> ScalarAsync<T>(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task QuartzSchema_IsAResource_ForResourcesSetup()
    {
        var resource = fixture.Host.Services.GetServices<IStatefulResource>().Single(r => r.Name == "Quartz job store");

        await resource.Check(TestContext.Current.CancellationToken);
        await resource.Setup(TestContext.Current.CancellationToken); // idempotent on a provisioned database
    }
}
