using Frappe.Geo.Contracts.Holidays;
using Frappe.Geo.Infrastructure.Calendars;
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
    /// <summary>The durable name of Geo's consumer of <see cref="HolidaysChanged"/>; forever.</summary>
    public const string CalendarCacheConsumer = "geo-calendar-cache";

    /// <summary>The relay of Geo's outbox and the purger of its outbox and inbox; the host also adds <see cref="AddGeoMessagePurgeJob"/>.</summary>
    public static IServiceCollection AddGeoMessaging(this IServiceCollection services) =>
        services.AddOutboxRelay<GeoDbContext>(GeoDbContext.Module).AddMessagePurge<GeoDbContext>(GeoDbContext.Module);

    /// <summary>
    /// Geo's own consumer of <see cref="HolidaysChanged"/> (<see cref="CalendarCacheConsumer"/>), which evicts the cached
    /// calendar the event names once it is committed. It writes nothing in its inbox transaction, so it has only that
    /// after-commit step, registered under its name: another module consuming the same event in this host keeps its own. The backplane carries the eviction to every
    /// process sharing the cache, so one host consuming it is enough.
    /// </summary>
    public static IServiceCollection AddGeoCalendarCacheInvalidation(this IServiceCollection services) =>
        services.AddGeoCalendarCacheHandlers()
            .AddIntegrationEventConsumer<GeoDbContext, HolidaysChanged>(GeoDbContext.Module, CalendarCacheConsumer);

    /// <summary>The step of <see cref="AddGeoCalendarCacheInvalidation"/> without the consumer, for a host or test that drives it itself.</summary>
    public static IServiceCollection AddGeoCalendarCacheHandlers(this IServiceCollection services) =>
        services.AddAfterCommitHandler<HolidaysChanged, CalendarCacheInvalidation>(CalendarCacheConsumer);

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
