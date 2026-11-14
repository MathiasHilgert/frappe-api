using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;

namespace Frappe.Scheduling;

/// <summary>
/// Durable jobs on a cron schedule that each environment may override, the one way every module schedules its work.
/// The expression is read from <c>{section}:Cron</c> (default <c>defaultCron</c>) and checked when the host starts: one
/// Quartz cannot read fails naming the key.
/// </summary>
public static class CronJobs
{
    /// <summary>
    /// Adds <typeparamref name="TJob"/> under <paramref name="key"/> on its schedule, and once as the host starts when
    /// <paramref name="runOnStartup"/> (so a fresh database does not wait for the first tick).
    /// </summary>
    public static IQuartzBuilder AddCronJob<TJob>(this IQuartzBuilder quartz, JobKey key, string section, string defaultCron, bool runOnStartup = false)
        where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(quartz);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        quartz.Services.AddOptions<CronScheduleOptions>(section)
            .BindConfiguration(section)
            .PostConfigure(options => options.Cron = string.IsNullOrWhiteSpace(options.Cron) ? defaultCron : options.Cron)
            .Validate(options => CronExpression.TryParse(options.Cron, out _), $"Configuration '{section}:Cron' must be a Quartz cron expression, seconds first.")
            .ValidateOnStart();

        quartz.AddJob<TJob>(job => job.WithIdentity(key).StoreDurably(true));
        if (runOnStartup)
        {
            quartz.AddTrigger(trigger => trigger.ForJob(key).WithIdentity(key.Name + "-on-startup", key.Group).StartNow());
        }

        quartz.AddTrigger((provider, trigger) => trigger
            .ForJob(key)
            .WithIdentity(key.Name + "-scheduled", key.Group)
            .WithCronSchedule(provider.GetRequiredService<IOptionsMonitor<CronScheduleOptions>>().Get(section).Cron!));
        return quartz;
    }
}
