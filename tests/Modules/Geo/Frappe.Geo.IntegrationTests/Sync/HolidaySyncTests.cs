using Frappe.Geo.Domain.Holidays;
using Frappe.Geo.Infrastructure.Sync;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.IntegrationTests.Sync;

/// <summary>The national holiday calendar through the same pipeline: next year's file is optional, and a change is announced.</summary>
public sealed class HolidaySyncTests
{
    private static async Task<(HolidayCalendar? Calendar, List<OutboxMessage> Outbox, List<SyncRunRow> Runs)> StoredAsync(GeoDatabase database)
    {
        await using var db = database.NewContext();
        var calendar = await db.Set<HolidayCalendar>().AsNoTracking().SingleOrDefaultAsync();
        return (calendar, await db.Set<OutboxMessage>().AsNoTracking().ToListAsync(), await db.Set<SyncRunRow>().AsNoTracking().OrderBy(r => r.StartedAt).ToListAsync());
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task This_year_is_applied_while_next_year_is_not_published_and_the_change_is_announced(GeoDatabase database)
    {
        var outcome = await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var (calendar, outbox, runs) = await StoredAsync(database);
        await Assert.That(outcome).IsEqualTo(SyncOutcome.Applied);
        await Assert.That((calendar!.Jurisdiction.Value, calendar.Year.Value)).IsEqualTo(("AR", 2026));
        await Assert.That(calendar.Effective.Count).IsEqualTo(32);
        await Assert.That(runs.Single().Inserted).IsEqualTo(1);
        await Assert.That(outbox.Single().Type).IsEqualTo("frappe.geo.holidays_changed.v1");
        await Assert.That(outbox.Single().Data).Contains("\"AR\"");
        await Assert.That(System.Text.Json.JsonElement.Parse(outbox.Single().Data).GetProperty("scope").GetString()).IsEqualTo("general");
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_calendar_stored_for_the_first_time_is_announced_even_when_it_has_no_observances(GeoDatabase database)
    {
        // A reader may have cached "not covered" for this year: its first calendar, empty or not, must evict that answer.
        await using (var db = database.NewContext())
        {
            var banking = CalendarScope.Create("banking").Value;
            var calendar = HolidayCalendar.Open(Jurisdiction.FromKey("AR"), HolidayYear.Create(2027).Value, banking);
            calendar.Import([]);
            db.Add(calendar);
            await db.SaveChangesAsync();
        }

        var (_, outbox, _) = await StoredAsync(database);
        var announced = System.Text.Json.JsonElement.Parse(outbox.Single().Data);
        await Assert.That(outbox.Single().Type).IsEqualTo("frappe.geo.holidays_changed.v1");
        await Assert.That(announced.GetProperty("year").GetInt32()).IsEqualTo(2027);
        await Assert.That(announced.GetProperty("scope").GetString()).IsEqualTo("banking");
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_republished_calendar_with_the_same_holidays_announces_nothing(GeoDatabase database)
    {
        var government = new SourceServer("holidays");
        await SourceSync.HolidaysAsync(database, government);
        government.Replace("holidays-2026-es.json", json => json.Replace("\"dateModified\"", "\"dateModifiedAgain\"", StringComparison.Ordinal));

        var outcome = await SourceSync.HolidaysAsync(database, government);

        var (_, outbox, runs) = await StoredAsync(database);
        await Assert.That(outcome).IsEqualTo(SyncOutcome.Applied);
        await Assert.That(runs[^1].Updated).IsEqualTo(0);
        await Assert.That(outbox.Count).IsEqualTo(1);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_missing_current_year_is_not_published_and_keeps_nothing(GeoDatabase database)
    {
        var government = new SourceServer("holidays");
        government.Unpublish("holidays-2026-es.json");

        var outcome = await SourceSync.HolidaysAsync(database, government);

        var (calendar, _, runs) = await StoredAsync(database);
        await Assert.That(outcome).IsEqualTo(SyncOutcome.NotPublished);
        await Assert.That(runs.Single().Error).Contains("holidays-2026");
        await Assert.That(calendar).IsNull();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_withdrawn_next_year_file_is_read_without_it_instead_of_refusing_the_run(GeoDatabase database)
    {
        var government = new SourceServer("holidays");
        government.Add("holidays-2027-es.json", government.Content("holidays-2026-es.json").Replace("2026-", "2027-", StringComparison.Ordinal));
        await SourceSync.HolidaysAsync(database, government);
        government.Unpublish("holidays-2027-es.json");

        var outcome = await SourceSync.HolidaysAsync(database, government);

        await Assert.That(outcome).IsEqualTo(SyncOutcome.Applied);
    }
}
