using Frappe.Geo.Application.Sync;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Infrastructure.Territory;
using Frappe.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>How sources join the pipeline: each source's adapter, its kind's target, the shared fetcher, and its job.</summary>
public static class SnapshotSyncRegistration
{
    /// <summary>The Quartz group of Geo's jobs.</summary>
    internal const string JobGroup = "geo";

    /// <summary>
    /// Lets country packages register their sources (<see cref="IGeoSources"/>). Their jobs are added by
    /// <see cref="AddGeoSyncJobs"/> inside the host's scheduler configuration.
    /// </summary>
    public static IServiceCollection AddGeoSyncs(this IServiceCollection services, Action<IGeoSources> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!services.Any(d => d.ServiceType == typeof(ConditionalFetcher)))
        {
            // The shared fetcher, behind the standard resilience pipeline (timeouts, retries with jitter).
            services.AddHttpClient<ConditionalFetcher>(http => http.Timeout = TimeSpan.FromMinutes(2)).AddStandardResilienceHandler();
        }

        sources(new GeoSources(services, SourceJobsOf(services)));
        return services;
    }

    /// <summary>
    /// Every registered source's sync as a durable job, once as the host starts and then on its schedule.
    /// </summary>
    public static IQuartzBuilder AddGeoSyncJobs(this IQuartzBuilder quartz)
    {
        ArgumentNullException.ThrowIfNull(quartz);
        foreach (var registration in RegistrationsOf(quartz.Services))
        {
            registration(quartz);
        }
        return quartz;
    }

    private static List<Action<IQuartzBuilder>> RegistrationsOf(IServiceCollection services) => SourceJobsOf(services).Jobs;

    private static SourceJobs SourceJobsOf(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(SourceJobs))?.ImplementationInstance is SourceJobs existing)
        {
            return existing;
        }

        var jobs = new SourceJobs();
        services.AddSingleton(jobs);
        return jobs;
    }

    /// <summary>The jobs the sources asked for, kept until the scheduler is configured, and which source each one syncs.</summary>
    private sealed class SourceJobs
    {
        public List<Action<IQuartzBuilder>> Jobs { get; } = [];

        public List<SourceJob> Sources { get; } = [];
    }

    /// <summary>A source's sync job: its name, and how to read the source's id (an instance property of the adapter).</summary>
    private sealed record SourceJob(string Name, Func<IServiceProvider, string> SourceId);

    private sealed class GeoSources(IServiceCollection services, SourceJobs jobs) : IGeoSources
    {
        public IServiceCollection Services => services;

        public IGeoSources Territory<TSource>(string key, string section, string defaultCron)
            where TSource : class, ISnapshotSource<TerritorySnapshot>
        {
            services.TryAddScoped<ISnapshotTarget<TerritorySnapshot>, TerritoryTarget>();
            return Add<TerritorySnapshot, TSource>(key, section, defaultCron);
        }

        private GeoSources Add<TSnapshot, TSource>(string key, string section, string defaultCron)
            where TSource : class, ISnapshotSource<TSnapshot>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            services.TryAddScoped<TSource>();
            services.TryAddSingleton<SyncMetrics>();
            services.TryAddScoped<SnapshotSync<TSnapshot, TSource>>();
            var name = "sync-" + key;
            jobs.Jobs.Add(quartz => quartz.AddCronJob<SnapshotSyncJob<TSnapshot, TSource>>(new JobKey(name, JobGroup), section, defaultCron, runOnStartup: true));
            jobs.Sources.Add(new SourceJob(name, provider => provider.GetRequiredService<TSource>().Id));
            return this;
        }
    }
}
