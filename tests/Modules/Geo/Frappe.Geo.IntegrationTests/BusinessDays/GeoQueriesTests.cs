using Frappe.Geo.Application;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Holidays.GetHolidays;
using Frappe.Geo.Contracts.Queries;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Geo.IntegrationTests.BusinessDays;

/// <summary>The public contract other modules call in process, over the same use cases as the HTTP API.</summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class GeoQueriesTests(GeoDatabase database, ValkeyCache valkey)
{
    private static readonly SemaphoreSlim Once = new(1, 1);
    private static readonly HashSet<string> Synced = [];

    private async Task<T> AskAsync<T>(Func<IGeoQueries, Task<T>> question)
    {
        await Once.WaitAsync();
        try
        {
            if (Synced.Add(database.Token))
            {
                await SourceSync.TerritoryAsync(database, new SourceServer());
                await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
                await SourceSync.ProvincialHolidaysAsync(database, SourceSync.Calendarios());
                await SourceSync.BankingHolidaysAsync(database, SourceSync.Bcra());
            }
        }
        finally
        {
            Once.Release();
        }

        await using var services = GeoServices.Build(database, valkey);
        await using var scope = services.CreateAsyncScope();
        return await question(scope.ServiceProvider.GetRequiredService<IGeoQueries>());
    }

    [Test]
    public async Task A_country_and_a_subdivision_are_found_with_what_geo_covers()
    {
        var argentina = await AskAsync(q => q.FindCountryAsync("ar", CancellationToken.None));
        var nowhere = await AskAsync(q => q.FindCountryAsync("ZZ", CancellationToken.None));
        var cordoba = await AskAsync(q => q.FindSubdivisionAsync("AR-X", CancellationToken.None));
        var malformed = await AskAsync(q => q.FindSubdivisionAsync("cordoba", CancellationToken.None));

        await Assert.That(argentina!.Code).IsEqualTo("AR");
        await Assert.That(argentina.Name).IsEqualTo("Argentina");
        await Assert.That(argentina.BusinessDayPolicies).IsEquivalentTo([BusinessDayPolicy.Banking, BusinessDayPolicy.Labor, BusinessDayPolicy.Commercial]);
        await Assert.That(argentina.Holidays.Select(h => h.Scope)).IsEquivalentTo(["banking", "general"]);
        await Assert.That(nowhere).IsNull();
        await Assert.That(cordoba!.CountryCode).IsEqualTo("AR");
        await Assert.That(malformed).IsNull();
    }

    [Test]
    public async Task Business_days_and_day_kinds_answer_with_their_coverage()
    {
        var bankWorkers = await AskAsync(q => q.IsBusinessDayAsync("AR", new DateOnly(2026, 11, 6), BusinessDayPolicy.Banking, CancellationToken.None));
        var next = await AskAsync(q => q.NextBusinessDaysAsync("AR", new DateOnly(2026, 11, 6), 3, BusinessDayPolicy.Banking, CancellationToken.None));
        var tourist = await AskAsync(q => q.GetDayKindAsync("AR", new DateOnly(2026, 3, 23), CancellationToken.None));
        var uruguay = await AskAsync(q => q.IsBusinessDayAsync("UY", new DateOnly(2026, 11, 6), BusinessDayPolicy.Commercial, CancellationToken.None));
        var later = await AskAsync(q => q.NextBusinessDaysAsync("AR", new DateOnly(2026, 12, 28), 10, BusinessDayPolicy.Commercial, CancellationToken.None));
        var unknown = await AskAsync(q => q.GetDayKindAsync("AR-Q9", new DateOnly(2026, 3, 23), CancellationToken.None));

        await Assert.That(bankWorkers).IsEqualTo(new BusinessDayAnswer(CalendarStatus.Covered, false, false, DayKind.Holiday));
        await Assert.That(next.Dates).IsEquivalentTo([new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 13)]);
        await Assert.That(tourist).IsEqualTo(new DayKindAnswer(CalendarStatus.Covered, DayKind.TouristNonWorkingDay, false));
        await Assert.That(uruguay.Status).IsEqualTo(CalendarStatus.NotCovered);
        await Assert.That((later.Status, later.Dates.Count)).IsEqualTo((CalendarStatus.NotCovered, 0));
        await Assert.That(unknown.Status).IsEqualTo(CalendarStatus.UnknownJurisdiction);
    }

    [Test]
    public async Task Questions_answered_not_covered_are_counted_by_country_without_the_subdivision()
    {
        await AskAsync(geo => geo.FindCountryAsync("AR", CancellationToken.None)); // the territory, so AR-B resolves
        await using var services = GeoServices.Build(database, valkey);
        using var capture = new MetricCapture(services.GetRequiredService<GeoMetrics>().Meter);
        await using var scope = services.CreateAsyncScope();
        var geo = scope.ServiceProvider.GetRequiredService<IGeoQueries>();

        await geo.IsBusinessDayAsync("UY", new DateOnly(2026, 7, 15), BusinessDayPolicy.Commercial, CancellationToken.None);
        await geo.NextBusinessDaysAsync("AR-X", new DateOnly(2040, 1, 2), 3, BusinessDayPolicy.Commercial, CancellationToken.None);
        await geo.IsBusinessDayAsync("AR", new DateOnly(2040, 1, 2), BusinessDayPolicy.Commercial, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetHolidays, HolidayCalendarView>>()
            .HandleAsync(new GetHolidays("UY", "2026"), CancellationToken.None);

        await Assert.That(capture.Sum(GeoMetrics.NotCoveredMetric, (GeoMetrics.CountryTag, "UY"), (GeoMetrics.ErrorCodeTag, "geo.business_days.not_covered"))).IsEqualTo(1);
        await Assert.That(capture.Sum(GeoMetrics.NotCoveredMetric, (GeoMetrics.CountryTag, "UY"), (GeoMetrics.ErrorCodeTag, "geo.holidays.not_covered"))).IsEqualTo(1);
        await Assert.That(capture.Sum(GeoMetrics.NotCoveredMetric, (GeoMetrics.CountryTag, "AR"), (GeoMetrics.ErrorCodeTag, "geo.holidays.not_covered"))).IsEqualTo(2);
        await Assert.That(capture.Measurements.SelectMany(m => m.Tags.Values).OfType<string>()).DoesNotContain("AR-X");
    }
}
