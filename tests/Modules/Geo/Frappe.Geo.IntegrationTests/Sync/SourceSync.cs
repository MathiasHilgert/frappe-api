using System.Diagnostics.Metrics;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Countries.Argentina.Holidays;
using Frappe.Geo.Countries.Argentina.Territory;
using Frappe.Geo.Infrastructure.Holidays;
using Frappe.Geo.Infrastructure.Persistence;
using Frappe.Geo.Infrastructure.Sync;
using Frappe.Geo.Infrastructure.Territory;
using Frappe.Geo.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using Polly;

namespace Frappe.Geo.IntegrationTests.Sync;

/// <summary>Runs the real pipeline with a real Argentine adapter against an in-process source and a real database.</summary>
internal static class SourceSync
{
    /// <summary>
    /// The fetcher's handler chain as in production: a resilience pipeline (here only its timeout, short, so a test can
    /// trip it) in front of the server, so the pipeline's own exceptions reach the sync as they would in the Worker.
    /// </summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>The year the holiday source believes it is: the fixture is the real 2026 file.</summary>
    public static readonly IClock In2026 = new FixedClock(Instant.FromUtc(2026, 11, 13, 12, 0));

    public static Task<SyncOutcome> TerritoryAsync(GeoDatabase database, SourceServer georef) =>
        RunAsync(database, georef, new GeorefTerritorySource(Options.Create(new GeorefOptions())), db => new TerritoryTarget(db, SystemClock.Instance));

    public static Task<SyncOutcome> HolidaysAsync(GeoDatabase database, SourceServer government, SyncMetrics? metrics = null) =>
        RunAsync(database, government, new OfficialHolidaySource(Options.Create(new OfficialHolidaysOptions()), In2026), db => new HolidayTarget(new EfHolidayCalendars(db)), metrics);

    /// <summary>
    /// The sync metrics of a Worker over <paramref name="database"/>, on a meter factory of their own (dispose the
    /// provider) so a test listens only to its own runs.
    /// </summary>
    public static (SyncMetrics Metrics, Meter Meter, ServiceProvider Provider) Metrics(GeoDatabase database, IClock clock)
    {
        var provider = new ServiceCollection().AddMetrics().AddScoped(_ => database.NewContext()).BuildServiceProvider();
        var geo = new GeoMetrics(provider.GetRequiredService<IMeterFactory>());
        return (new SyncMetrics(geo, provider.GetRequiredService<IServiceScopeFactory>(), clock), geo.Meter, provider);
    }

    /// <summary>Metrics for the runs whose test does not observe them: nothing listens to their meter.</summary>
    private static readonly SyncMetrics Unobserved =
        new(new GeoMetrics(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>()), new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), SystemClock.Instance);

    private static async Task<SyncOutcome> RunAsync<TSnapshot, TSource>(GeoDatabase database, SourceServer server, TSource source, Func<GeoDbContext, ISnapshotTarget<TSnapshot>> target, SyncMetrics? metrics = null)
        where TSource : ISnapshotSource<TSnapshot>
    {
        var resilience = new ResilienceHandler(new ResiliencePipelineBuilder<HttpResponseMessage>().AddTimeout(AttemptTimeout).Build())
        {
            InnerHandler = server,
        };
        await using var db = database.NewContext();
        var sync = new SnapshotSync<TSnapshot, TSource>(
            source,
            target(db),
            new ConditionalFetcher(new HttpClient(resilience, disposeHandler: false)),
            db,
            SystemClock.Instance,
            metrics ?? Unobserved,
            NullLogger<SnapshotSync<TSnapshot, TSource>>.Instance);
        return await sync.RunAsync(CancellationToken.None);
    }

    private sealed class FixedClock(Instant now) : IClock
    {
        public Instant GetCurrentInstant() => now;
    }
}
