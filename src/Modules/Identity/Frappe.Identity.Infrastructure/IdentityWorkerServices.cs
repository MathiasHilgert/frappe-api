using Frappe.Identity.Infrastructure.Owners;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Retention;
using Frappe.Messaging.Abstractions;
using Frappe.Modules;
using Frappe.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quartz;

namespace Frappe.Identity.Infrastructure;

/// <summary>
/// What only the Worker runs for Identity: the relay of its outbox, the purge of its messages and the daily retention job (S16): the sweep of ended
/// sessions and the purge of stale data. The relay and the message purge are declared through the broker-free messaging abstractions, so
/// composing it in a host without a transport records the declarations and runs nothing; the door's <c>IdentityModule.Worker</c> composes it.
/// </summary>
public sealed class IdentityWorkerServices : IModule
{
    /// <inheritdoc />
    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOutboxRelay<IdentityDbContext>(IdentityCoreServices.Name);
        services.AddMessagePurge<IdentityDbContext>(IdentityCoreServices.Name);
        AddRetention(services);
    }

    /// <summary>
    /// The daily retention at 03:00 UTC (overridable by <c>Identity:Retention:Cron</c>) on the clustered scheduler. The job is added through
    /// <c>ConfigureAllQuartzSchedulers</c>, which every host's scheduler applies, so no module configures <c>QuartzOptions</c> itself.
    /// </summary>
    private static void AddRetention(IServiceCollection services)
    {
        services.AddOptions<IdentityRetentionOptions>().BindConfiguration(IdentityRetentionOptions.Section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IdentityRetentionOptions>, IdentityRetentionOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SessionSweeper>();
        services.AddSingleton<StaleRowsPurger>();
        services.AddSingleton<PendingOwnerPurger>();
        services.AddSingleton<IRetentionStep>(provider => provider.GetRequiredService<SessionSweeper>());
        services.AddSingleton<IRetentionStep>(provider => provider.GetRequiredService<StaleRowsPurger>());
        services.AddSingleton<IRetentionStep>(provider => provider.GetRequiredService<PendingOwnerPurger>());
        services.ConfigureAllQuartzSchedulers(quartz => quartz.AddCronJob<IdentityRetentionJob>(
            IdentityRetentionJob.Key, IdentityRetentionOptions.Section, "0 0 3 * * ?"));
    }
}
