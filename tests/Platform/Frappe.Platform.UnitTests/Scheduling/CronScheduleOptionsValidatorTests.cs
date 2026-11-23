using Frappe.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;

namespace Frappe.Platform.UnitTests.Scheduling;

/// <summary>A job's cron is checked at startup, naming the full key of its section (CFG-03, CFG-06).</summary>
public sealed class CronScheduleOptionsValidatorTests
{
    private const string Section = "Orders:MessagePurge";
    private const string DefaultCron = "0 30 3 * * ?";

    private static ServiceProvider Build(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddQuartz(quartz => quartz.AddCronJob<NoopJob>(new JobKey("purge", "orders"), Section, DefaultCron));
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task A_cron_quartz_cannot_read_fails_naming_the_key()
    {
        // Arrange
        await using var services = Build(($"{Section}:Cron", "every day"));

        // Act
        var thrown = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptionsMonitor<CronScheduleOptions>>().Get(Section));

        // Assert
        await Assert.That(thrown.Failures.Single())
            .Contains("Configuration 'Orders:MessagePurge:Cron' must be a Quartz cron expression, seconds first.");
    }

    [Test]
    public async Task A_missing_cron_with_no_default_fails_naming_the_key()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddQuartz(quartz => quartz.AddCronJob<NoopJob>(new JobKey("purge", "orders"), Section, string.Empty));
        await using var provider = services.BuildServiceProvider();

        // Act
        var thrown = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<CronScheduleOptions>>().Get(Section));

        // Assert
        await Assert.That(thrown.Failures.Single())
            .Contains("Configuration 'Orders:MessagePurge:Cron' must be a Quartz cron expression, seconds first.");
    }

    [Test]
    public async Task A_missing_cron_takes_the_jobs_default()
    {
        // Arrange
        await using var services = Build();

        // Act
        var options = services.GetRequiredService<IOptionsMonitor<CronScheduleOptions>>().Get(Section);

        // Assert
        await Assert.That(options.Cron).IsEqualTo(DefaultCron);
    }

    [Test]
    public async Task A_configured_quartz_cron_overrides_the_default()
    {
        // Arrange
        await using var services = Build(($"{Section}:Cron", "0 0 5 * * ?"));

        // Act
        var options = services.GetRequiredService<IOptionsMonitor<CronScheduleOptions>>().Get(Section);

        // Assert
        await Assert.That(options.Cron).IsEqualTo("0 0 5 * * ?");
    }

    [Test]
    public async Task Another_jobs_options_are_validated_under_its_own_section()
    {
        // Arrange
        var services = new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([KeyValuePair.Create<string, string?>("Orders:Other:Cron", "nope")])
            .Build());
        services.AddQuartz(quartz => quartz
            .AddCronJob<NoopJob>(new JobKey("purge", "orders"), Section, DefaultCron)
            .AddCronJob<NoopJob>(new JobKey("other", "orders"), "Orders:Other", DefaultCron));
        await using var provider = services.BuildServiceProvider();

        // Act
        var valid = provider.GetRequiredService<IOptionsMonitor<CronScheduleOptions>>().Get(Section);
        var thrown = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<CronScheduleOptions>>().Get("Orders:Other"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(valid.Cron).IsEqualTo(DefaultCron);
            await Assert.That(thrown.Failures.Single()).Contains("Orders:Other:Cron");
        }
    }

    /// <summary>A job that does nothing, only there to be scheduled.</summary>
    private sealed class NoopJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
