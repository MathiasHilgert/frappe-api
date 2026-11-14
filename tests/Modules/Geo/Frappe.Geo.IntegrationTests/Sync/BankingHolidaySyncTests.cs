using System.Net;
using Frappe.Geo.Domain.Holidays;
using Frappe.Geo.Infrastructure.Sync;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using static Frappe.Geo.IntegrationTests.Admin.AdminRequests;

namespace Frappe.Geo.IntegrationTests.Sync;

/// <summary>The BCRA's banking holidays through the same pipeline: a calendar of their own, beside the general one and apart from it.</summary>
[ClassDataSource<ValkeyCache>(Shared = SharedType.PerTestSession)]
public sealed class BankingHolidaySyncTests(ValkeyCache valkey)
{
    private static async Task<HolidayCalendar[]> CalendarsAsync(GeoDatabase database)
    {
        await using var db = database.NewContext();
        return await db.Set<HolidayCalendar>().AsNoTracking().ToArrayAsync();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task The_banking_calendar_is_stored_apart_and_the_general_one_is_unchanged(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
        var general = (await CalendarsAsync(database)).Single().Effective;

        var outcome = await SourceSync.BankingHolidaysAsync(database, SourceSync.Bcra());

        var calendars = await CalendarsAsync(database);
        var banking = calendars.Single(c => c.Scope.Value == "banking");
        await Assert.That(outcome).IsEqualTo(SyncOutcome.Applied);
        await Assert.That(calendars.Length).IsEqualTo(2);
        await Assert.That((banking.Jurisdiction.Value, banking.Year.Value)).IsEqualTo(("AR", 2026));
        await Assert.That(banking.Effective.Count).IsEqualTo(21);
        await Assert.That(banking.Effective.Single(o => o.Date == new NodaTime.LocalDate(2026, 11, 6)).LegalBasis!.Citation).IsEqualTo("CCT 18/75, art. 50");
        await Assert.That(calendars.Single(c => c.Scope == CalendarScope.General).Effective).IsEquivalentTo(general);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_banking_change_is_announced_with_its_scope(GeoDatabase database)
    {
        await SourceSync.BankingHolidaysAsync(database, SourceSync.Bcra());

        await using var db = database.NewContext();
        var announced = await db.Set<Frappe.Messaging.OutboxMessage>().AsNoTracking().ToListAsync();
        var payload = System.Text.Json.JsonElement.Parse(announced.Single().Data);
        await Assert.That(announced.Single().Type).IsEqualTo("frappe.geo.holidays_changed.v1");
        await Assert.That((payload.GetProperty("jurisdiction").GetString(), payload.GetProperty("scope").GetString())).IsEqualTo(("AR", "banking"));
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task The_holidays_endpoint_keeps_answering_the_general_calendar(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
        await SourceSync.BankingHolidaysAsync(database, SourceSync.Bcra());

        var answer = await SendAsync(database, valkey, HttpMethod.Get, "/v1/geo/countries/AR/holidays?year=2026");

        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(answer.Body.GetProperty("observances").GetArrayLength()).IsEqualTo(32);
    }
}
