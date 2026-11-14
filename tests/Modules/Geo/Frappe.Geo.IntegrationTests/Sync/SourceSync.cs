using System.Diagnostics.Metrics;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Countries.Argentina.Banking;
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

    public static Task<SyncOutcome> ProvincialHolidaysAsync(GeoDatabase database, SourceServer calendarios) =>
        RunAsync(database, calendarios, new ProvincialHolidaySource(Options.Create(new ProvincialHolidaysOptions()), In2026), db => new HolidayTarget(new EfHolidayCalendars(db)));

    public static Task<SyncOutcome> BankingHolidaysAsync(GeoDatabase database, SourceServer bcra, SyncMetrics? metrics = null) =>
        RunAsync(database, bcra, new BankingHolidaySource(Options.Create(new BankingHolidaysOptions()), In2026), db => new HolidayTarget(new EfHolidayCalendars(db)), metrics);

    /// <summary>The path of the BCRA's 2026 banking holidays page, as the server answers it.</summary>
    public const string BcraPage2026 = "consulta-feriados-bancarios-2026/";

    /// <summary>The BCRA's 2026 banking holidays page (the real, trimmed one of the fixture folder); 2027 is not published.</summary>
    public static SourceServer Bcra()
    {
        var server = new SourceServer("bcra");
        server.Add(BcraPage2026, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bcra", "consulta-feriados-bancarios-2026.html")));
        return server;
    }

    /// <summary>
    /// Calendarios Nacionales' 2026 provincial files: the trimmed real ones of the fixture folder, and for every other
    /// province a file with no own observance, built from Chubut's, so the required files are all published.
    /// </summary>
    public static SourceServer Calendarios()
    {
        var server = new SourceServer("calendarios-nacionales");
        var chubut = server.Content("2026/provincias/chubut.json");
        foreach (var (slug, code) in ArgentineProvinces.Codes.Where(p => !server.Serves($"2026/provincias/{p.Key}.json")))
        {
            server.Add($"2026/provincias/{slug}.json", chubut
                .Replace("\"slug\": \"chubut\"", $"\"slug\": \"{slug}\"", StringComparison.Ordinal)
                .Replace("\"code\": \"AR-U\"", $"\"code\": \"{code}\"", StringComparison.Ordinal));
        }

        return server;
    }

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
