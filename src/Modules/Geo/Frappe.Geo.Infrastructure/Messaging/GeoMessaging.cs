using Frappe.Geo.Infrastructure.Persistence;
using Frappe.Messaging.Hosting;
using Frappe.Messaging.Retention;
using Frappe.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Frappe.Geo.Infrastructure.Messaging;

/// <summary>Where Geo's integration events leave from: its outbox, relayed continuously, and its messages, purged nightly.</summary>
public static class GeoMessaging
{
    /// <summary>The relay of Geo's outbox and the purger of its outbox and inbox; the host also adds <see cref="AddGeoMessagePurgeJob"/>.</summary>
    public static IServiceCollection AddGeoMessaging(this IServiceCollection services) =>
        services.AddOutboxRelay<GeoDbContext>(GeoDbContext.Module).AddMessagePurge<GeoDbContext>(GeoDbContext.Module);

    /// <summary>The purge as job <c>purge-messages</c>, on <c>Geo:MessagePurge:Cron</c> (nightly by default).</summary>
    public static IQuartzBuilder AddGeoMessagePurgeJob(this IQuartzBuilder quartz) =>
        quartz.AddCronJob<GeoMessagePurgeJob>(new JobKey("purge-messages", "geo"), "Geo:MessagePurge", "0 30 3 * * ?", runOnStartup: false);

    [DisallowConcurrentExecution]
    private sealed class GeoMessagePurgeJob(MessagePurger<GeoDbContext> purger) : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
            await purger.PurgeAsync(cancellationToken);
    }
}
