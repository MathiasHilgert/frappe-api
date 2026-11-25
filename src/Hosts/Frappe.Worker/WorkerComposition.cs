using Frappe.Caching;
using Frappe.Health;
using Frappe.Messaging;
using Frappe.Messaging.Hosting;
using Frappe.Modules;
using Frappe.Notifications.Infrastructure.Retention;
using Frappe.Observability.Hosting;
using Frappe.Scheduling;
using Quartz.Diagnostics;

namespace Frappe.Worker;

/// <summary>
/// Everything the Worker runs: scheduled jobs on the clustered Quartz store, the outbox relays and the message consumers, each
/// contributed by the modules <see cref="Modules.All"/> lists. A module adds its Quartz jobs (<c>services.ConfigureAllQuartzSchedulers</c>
/// with <c>AddCronJob</c>) and its meter views (<c>ConfigureOpenTelemetryMeterProvider</c>) through its own services, so the Worker names none.
/// Every setting is bound to an options type and validated when the host starts, before it serves anything: a missing or invalid
/// one fails naming its configuration key. <c>Caching:InstanceName</c> (default <c>frappe</c>) and <c>Messaging:Namespace</c>
/// (default none) isolate several Workers sharing one Valkey or NATS. The Worker serves no HTTP: it opens TCP probe ports instead.
/// </summary>
public static class WorkerComposition
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>Composes the Worker with the modules of <see cref="Modules.All"/>.</summary>
        public IHostApplicationBuilder AddFrappeWorker() => builder.AddFrappeWorker(Modules.All);

        /// <summary>Composes the Worker with <paramref name="modules"/>, which tests replace to prove the composition.</summary>
        public IHostApplicationBuilder AddFrappeWorker(IReadOnlyCollection<IModule> modules)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(modules);

            builder.AddFrappeTelemetry(
                "frappe-worker",
                traces => traces.AddSource([.. MessagingTelemetry.ActivitySources, QuartzInstrumentation.ActivitySourceName]),
                meters => meters.AddMeter(QuartzInstrumentation.MeterName));
            builder.Services
                .AddFrappeCaching()
                .AddJetStream("frappe-worker")
                .AddFrappeScheduling(_ => { })
                .AddTcpHealthProbes();

            foreach (var module in modules)
            {
                module.AddServices(builder.Services);
            }

            builder.Services.AddSingleton<IMessagePurge, NotificationsMessagePurge>();
            return builder;
        }
    }
}
