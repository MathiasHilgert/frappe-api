using Frappe.Caching;
using Frappe.Geo.Countries.Argentina;
using Frappe.Geo.Infrastructure;
using Frappe.Geo.Infrastructure.Messaging;
using Frappe.Geo.Infrastructure.Sync;
using Frappe.Health;
using Frappe.Messaging;
using Frappe.Messaging.Hosting;
using Frappe.Observability.Hosting;
using Frappe.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz.Diagnostics;

namespace Frappe.Worker;

/// <summary>
/// Everything the Worker runs: scheduled jobs, the outbox relays and message retention, each registered by a module.
/// Geo syncs the reference data of the countries composed here (<c>AddGeoSyncs</c>), each source on its own schedule.
/// Every setting is bound to an options type and validated when the host starts, before it serves anything: a missing
/// or invalid one fails naming its configuration key. <c>Caching:InstanceName</c> (default <c>frappe</c>) and
/// <c>Messaging:Namespace</c> (default none) isolate several Workers sharing one Valkey or NATS.
/// </summary>
public static class WorkerComposition
{
    public static IHostApplicationBuilder AddFrappeWorker(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddFrappeTelemetry(
            "frappe-worker",
            traces => traces.AddSource([.. MessagingTelemetry.ActivitySources, QuartzInstrumentation.ActivitySourceName]),
            meters => meters.AddMeter(QuartzInstrumentation.MeterName));
        builder.Services
            .AddFrappeCaching()
            .AddGeoCore()
            .AddArgentinaCalendar()
            .AddGeoSyncs(geo => geo.AddArgentina())
            .AddJetStream("frappe-worker")
            .AddGeoMessaging()
            .AddFrappeScheduling(quartz => quartz.AddGeoSyncJobs().AddGeoMessagePurgeJob())
            .AddTcpHealthProbes();

        return builder;
    }
}
