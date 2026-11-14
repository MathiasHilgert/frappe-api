using System.Net;
using Frappe.Geo.Contracts.Holidays;
using Frappe.Geo.Infrastructure.Messaging;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using Frappe.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using static Frappe.Geo.IntegrationTests.Admin.AdminRequests;

namespace Frappe.Geo.IntegrationTests.BusinessDays;

/// <summary>Business-day answers come from the platform cache, per stored calendar, until <see cref="HolidaysChanged"/> evicts it.</summary>
[ClassDataSource<ValkeyCache>(Shared = SharedType.PerTestSession)]
public sealed class CalendarCacheTests(ValkeyCache valkey)
{
    private static async Task<bool> BankingOpenOn6NovemberAsync(GeoDatabase database, ValkeyCache valkey)
    {
        var answer = await GeoApi.GetAsync(database, valkey, "/v1/geo/countries/AR/calendar/2026-11-06?policy=banking");
        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        return answer.Body.GetProperty("businessDay").GetBoolean();
    }

    private static async Task AfterCommitAsync(ServiceProvider services, HolidaysChanged changed)
    {
        await using var scope = services.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider.GetKeyedServices<IAfterCommitHandler<HolidaysChanged>>(GeoMessaging.CalendarCacheConsumer))
        {
            await handler.HandleAsync(changed, CancellationToken.None);
        }
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_change_shows_only_after_its_holidays_changed_evicts_the_cached_calendar(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
        await SourceSync.BankingHolidaysAsync(database, SourceSync.Bcra());
        await using var services = GeoServices.Build(database, valkey);
        var before = await BankingOpenOn6NovemberAsync(database, valkey);

        var put = await SendAsync(database, valkey, HttpMethod.Put, "/v1/geo/countries/AR/holidays/suppressions?scope=banking", new { date = "2026-11-06", name = "Día del Bancario" });
        var cached = await BankingOpenOn6NovemberAsync(database, valkey);
        await AfterCommitAsync(services, new HolidaysChanged("AR", 2026) { Scope = "general" });
        var otherScopeEvicted = await BankingOpenOn6NovemberAsync(database, valkey);
        await AfterCommitAsync(services, new HolidaysChanged("AR", 2026) { Scope = "banking" });
        var after = await BankingOpenOn6NovemberAsync(database, valkey);

        await Assert.That(put.Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(before).IsFalse();
        await Assert.That(cached).IsFalse();
        await Assert.That(otherScopeEvicted).IsFalse();
        await Assert.That(after).IsTrue();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_event_without_a_scope_evicts_every_scope_of_its_jurisdiction_and_year(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
        await SourceSync.BankingHolidaysAsync(database, SourceSync.Bcra());
        await using var services = GeoServices.Build(database, valkey);
        await BankingOpenOn6NovemberAsync(database, valkey);

        await SendAsync(database, valkey, HttpMethod.Put, "/v1/geo/countries/AR/holidays/suppressions?scope=banking", new { date = "2026-11-06", name = "Día del Bancario" });
        await AfterCommitAsync(services, new HolidaysChanged("AR", 2026));

        await Assert.That(await BankingOpenOn6NovemberAsync(database, valkey)).IsTrue();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_calendar_not_covered_is_cached_as_missing_until_evicted(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
        await using var services = GeoServices.Build(database, valkey);
        var missing = await GeoApi.GetAsync(database, valkey, "/v1/geo/countries/AR/calendar/2026-11-06?policy=banking");

        await SourceSync.BankingHolidaysAsync(database, SourceSync.Bcra());
        await AfterCommitAsync(services, new HolidaysChanged("AR", 2026) { Scope = "banking" });

        await Assert.That(Outcome(missing)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holidays.not_covered"));
        await Assert.That(await BankingOpenOn6NovemberAsync(database, valkey)).IsFalse();
    }
}
