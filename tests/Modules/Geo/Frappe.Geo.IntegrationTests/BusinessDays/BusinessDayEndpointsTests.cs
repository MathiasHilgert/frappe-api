using System.Net;
using System.Text.Json;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using static Frappe.Geo.IntegrationTests.Admin.AdminRequests;

namespace Frappe.Geo.IntegrationTests.BusinessDays;

/// <summary>
/// The business-day endpoints after real syncs of the territory and the 2026 national, provincial and banking (BCRA)
/// calendars. 2026: 6 November is the bank workers' day (sector rule), 9 November a national holiday, 10 November a
/// BCRA-only banking holiday and a holiday in Córdoba (AR-X), 23 March a tourist non-working day. Read-only.
/// </summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class BusinessDayEndpointsTests(GeoDatabase database, ValkeyCache valkey)
{
    private static readonly SemaphoreSlim Once = new(1, 1);
    private static readonly HashSet<string> Synced = [];

    private async Task<GeoApi.Answer> GetAsync(string path, string? acceptLanguage = null)
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

        return await GeoApi.GetAsync(database, valkey, path, acceptLanguage);
    }

    private async Task<bool> IsBusinessDayAsync(string jurisdictionPath, string date, string policy)
    {
        var answer = await GetAsync($"/v1/geo/{jurisdictionPath}/calendar/{date}?policy={policy}");
        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        return answer.Body.GetProperty("businessDay").GetBoolean();
    }

    private async Task<string[]> NextAsync(string jurisdictionPath, string from, int count, string policy)
    {
        var answer = await GetAsync($"/v1/geo/{jurisdictionPath}/business-days?from={from}&count={count}&policy={policy}");
        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        return [.. answer.Body.GetProperty("dates").EnumerateArray().Select(d => d.GetString()!)];
    }

    [Test]
    public async Task Calendar_answers_may_be_reused_for_five_minutes_since_the_server_evicts_in_seconds()
    {
        var answer = await GetAsync("/v1/geo/countries/AR/calendar/2026-07-15?policy=commercial");

        await Assert.That(answer.CacheControl).Contains("max-age=300");
    }

    [Test]
    public async Task Banking_closes_on_a_bcra_only_day_and_on_the_bank_workers_day_that_commerce_works()
    {
        await Assert.That(await IsBusinessDayAsync("countries/AR", "2026-11-10", "banking")).IsFalse();
        await Assert.That(await IsBusinessDayAsync("countries/AR", "2026-11-06", "banking")).IsFalse();
        await Assert.That(await IsBusinessDayAsync("countries/AR", "2026-11-10", "commercial")).IsTrue();
        await Assert.That(await IsBusinessDayAsync("countries/AR", "2026-11-06", "commercial")).IsTrue();
        await Assert.That(await IsBusinessDayAsync("countries/AR", "2026-11-11", "banking")).IsTrue();
        var banking = await GetAsync("/v1/geo/countries/AR/calendar/2026-11-06?policy=banking");
        await Assert.That(banking.Body.GetProperty("attributions").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task Commerce_ignores_non_working_days_that_close_banks()
    {
        await Assert.That(await IsBusinessDayAsync("countries/AR", "2026-03-23", "commercial")).IsTrue();
        await Assert.That(await IsBusinessDayAsync("countries/AR", "2026-03-23", "banking")).IsFalse();
        await Assert.That(await IsBusinessDayAsync("countries/AR", "2026-06-15", "commercial")).IsFalse();
        await Assert.That(await IsBusinessDayAsync("countries/AR", "2026-11-14", "commercial")).IsFalse();
    }

    [Test]
    public async Task Labor_answers_the_kind_of_the_day()
    {
        var tourist = await GetAsync("/v1/geo/countries/AR/calendar/2026-03-23?policy=labor");
        var holiday = await GetAsync("/v1/geo/countries/AR/calendar/2026-06-15?policy=labor");
        var ordinary = await GetAsync("/v1/geo/countries/AR/calendar/2026-06-16?policy=labor");

        await Assert.That(tourist.Body.GetProperty("kind").GetString()).IsEqualTo("tourist_non_working_day");
        await Assert.That(tourist.Body.GetProperty("businessDay").GetBoolean()).IsTrue();
        await Assert.That(tourist.Body.GetProperty("policy").GetString()).IsEqualTo("labor");
        await Assert.That(holiday.Body.GetProperty("kind").GetString()).IsEqualTo("holiday");
        await Assert.That(holiday.Body.GetProperty("businessDay").GetBoolean()).IsFalse();
        await Assert.That(holiday.Body.GetProperty("observances")[0].GetProperty("movedFrom").GetString()).IsEqualTo("2026-06-17");
        await Assert.That(ordinary.Body.GetProperty("kind").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(ordinary.Body.GetProperty("observances").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task The_next_business_days_skip_the_weekend_and_the_holidays_of_the_policy()
    {
        await Assert.That(await NextAsync("countries/AR", "2026-11-06", 3, "banking")).IsEquivalentTo(["2026-11-11", "2026-11-12", "2026-11-13"]);
        await Assert.That(await NextAsync("countries/AR", "2026-11-06", 3, "commercial")).IsEquivalentTo(["2026-11-06", "2026-11-10", "2026-11-11"]);
    }

    [Test]
    public async Task A_subdivision_adds_its_own_holidays_to_its_country_s()
    {
        var day = await GetAsync("/v1/geo/subdivisions/AR-X/calendar/2026-11-10?policy=commercial");

        await Assert.That(day.Body.GetProperty("businessDay").GetBoolean()).IsFalse();
        await Assert.That(day.Body.GetProperty("covers").EnumerateArray().Select(c => c.GetString()!)).IsEquivalentTo(["AR", "AR-X"]);
        await Assert.That(day.Body.GetProperty("attributions").GetArrayLength()).IsEqualTo(2);
        await Assert.That(await NextAsync("subdivisions/AR-X", "2026-11-09", 1, "commercial")).IsEquivalentTo(["2026-11-11"]);
        await Assert.That(await NextAsync("countries/AR", "2026-11-09", 1, "commercial")).IsEquivalentTo(["2026-11-10"]);
    }

    [Test]
    public async Task What_geo_does_not_hold_is_not_covered_never_an_ordinary_day()
    {
        var noRules = await GetAsync("/v1/geo/countries/UY/calendar/2026-11-10?policy=banking");
        var noYear = await GetAsync("/v1/geo/countries/AR/calendar/2027-03-01?policy=commercial");
        var intoNoYear = await GetAsync("/v1/geo/countries/AR/business-days?from=2026-12-28&count=10&policy=commercial");

        await Assert.That(Outcome(noRules)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.business_days.not_covered"));
        await Assert.That(Outcome(noYear)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holidays.not_covered"));
        await Assert.That(Outcome(intoNoYear)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holidays.not_covered"));
    }

    [Test]
    public async Task Bad_inputs_answer_400_with_their_codes()
    {
        var policy = await GetAsync("/v1/geo/countries/AR/calendar/2026-11-10?policy=payroll");
        var missing = await GetAsync("/v1/geo/countries/AR/calendar/2026-11-10");
        var date = await GetAsync("/v1/geo/countries/AR/calendar/2026-02-30?policy=banking");
        var count = await GetAsync("/v1/geo/countries/AR/business-days?from=2026-11-06&count=61&policy=banking");
        var unknown = await GetAsync("/v1/geo/subdivisions/AR-Q9/calendar/2026-11-10?policy=banking");

        await Assert.That(Outcome(policy)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.business_day_policy.invalid"));
        await Assert.That(Outcome(missing)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.parameter.required"));
        await Assert.That(Outcome(date)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.date.invalid_format"));
        await Assert.That(Outcome(count)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.business_day_count.out_of_range"));
        await Assert.That(unknown.Status).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Missing_parameters_are_required_unreadable_ones_invalid_and_out_of_bounds_numbers_out_of_range()
    {
        var nothing = await GetAsync("/v1/geo/countries/AR/business-days");
        var unreadableCount = await GetAsync("/v1/geo/countries/AR/business-days?from=2026-11-06&count=many&policy=banking");
        var zero = await GetAsync("/v1/geo/countries/AR/business-days?from=2026-11-06&count=0&policy=banking");
        var badYear = await GetAsync("/v1/geo/countries/AR/holidays?year=26x");
        var farYear = await GetAsync("/v1/geo/countries/AR/holidays?year=2200");

        static string[] Required(GeoApi.Answer answer) =>
            [.. answer.Body.GetProperty("errors").EnumerateArray().Select(e => $"{e.GetProperty("code").GetString()}:{e.GetProperty("parameters").GetProperty("parameter").GetString()}")];
        await Assert.That(nothing.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(Required(nothing)).IsEquivalentTo(["geo.parameter.required:from", "geo.parameter.required:policy", "geo.parameter.required:count"]);
        await Assert.That(Outcome(unreadableCount)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.business_day_count.invalid"));
        await Assert.That(Outcome(zero)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.business_day_count.out_of_range"));
        await Assert.That(Outcome(badYear)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.holiday_year.invalid"));
        await Assert.That(Outcome(farYear)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.holiday_year.out_of_range"));
    }

    [Test]
    public async Task The_next_business_days_say_what_they_cover_and_credit_their_sources()
    {
        var banking = await GetAsync("/v1/geo/subdivisions/AR-X/business-days?from=2026-11-06&count=3&policy=banking");

        await Assert.That(banking.Body.GetProperty("covers").EnumerateArray().Select(c => c.GetString()!)).IsEquivalentTo(["AR", "AR-X"]);
        await Assert.That(banking.Body.GetProperty("attributions").EnumerateArray().Select(a => a.GetProperty("url").GetString()!))
            .IsEquivalentTo(["https://www.argentina.gob.ar/", "https://calendariosnacionales.com/ar/api/", "https://www.bcra.gob.ar/"]);
    }

    [Test]
    public async Task Holidays_answer_the_scope_asked_for_and_refuse_one_the_country_does_not_declare()
    {
        var banking = await GetAsync("/v1/geo/countries/AR/holidays?year=2026&scope=banking");
        var general = await GetAsync("/v1/geo/countries/AR/holidays?year=2026");
        var foo = await GetAsync("/v1/geo/countries/AR/holidays?year=2026&scope=foo");

        var bcraOnly = banking.Body.GetProperty("observances").EnumerateArray().Single(o => o.GetProperty("date").GetString() == "2026-11-10");
        await Assert.That(banking.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(banking.Body.GetProperty("scope").GetString()).IsEqualTo("banking");
        await Assert.That(general.Body.GetProperty("scope").GetString()).IsEqualTo("general");
        await Assert.That(banking.Body.GetProperty("attributions").EnumerateArray().Select(a => a.GetProperty("url").GetString()!)).IsEquivalentTo(["https://www.bcra.gob.ar/"]);
        await Assert.That(bcraOnly.GetProperty("legalBasis").GetProperty("citation").GetString()).IsEqualTo("BCRA, Comunicaciones C 101352, C 102390 y A 8487");
        await Assert.That(general.Body.GetProperty("observances").EnumerateArray().Any(o => o.GetProperty("date").GetString() == "2026-11-10")).IsFalse();
        await Assert.That(Outcome(foo)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.calendar_scope.invalid"));
        await Assert.That(foo.Body.GetProperty("errors")[0].GetProperty("parameters").GetProperty("scopes").EnumerateArray().Select(s => s.GetString()!)).IsEquivalentTo(["general", "banking"]);
    }

    [Test]
    public async Task Not_covered_names_the_calendar_actually_missing_and_quotes_codes_in_every_language()
    {
        var subdivision = await GetAsync("/v1/geo/subdivisions/AR-X/holidays?year=2027");
        var noRules = await GetAsync("/v1/geo/countries/UY/calendar/2026-11-10?policy=labor", "es");
        var noRulesPt = await GetAsync("/v1/geo/countries/UY/calendar/2026-11-10?policy=labor", "pt-BR");

        var missing = subdivision.Body.GetProperty("errors")[0];
        await Assert.That(missing.GetProperty("parameters").GetProperty("jurisdiction").GetString()).IsEqualTo("AR");
        await Assert.That(missing.GetProperty("code").GetString()).IsEqualTo("geo.holidays.not_covered");
        await Assert.That(missing.GetProperty("message").GetString()).DoesNotContain("AR-X");
        await Assert.That(noRules.Body.GetProperty("errors")[0].GetProperty("message").GetString()).Contains("“labor”");
        await Assert.That(noRulesPt.Body.GetProperty("errors")[0].GetProperty("message").GetString()).Contains("“labor”");
    }

    [Test]
    public async Task A_country_says_what_geo_covers_for_it()
    {
        var argentina = await GetAsync("/v1/geo/countries/AR");
        var uruguay = await GetAsync("/v1/geo/countries/UY");

        var capabilities = argentina.Body.GetProperty("capabilities");
        var holidays = capabilities.GetProperty("holidays").EnumerateArray()
            .ToDictionary(h => h.GetProperty("scope").GetString()!, h => h.GetProperty("years").EnumerateArray().Select(y => y.GetInt32()).ToArray());
        await Assert.That(holidays.Keys).IsEquivalentTo(["banking", "general"]);
        await Assert.That(holidays["general"]).IsEquivalentTo([2026]);
        await Assert.That(capabilities.GetProperty("businessDayPolicies").EnumerateArray().Select(p => p.GetString()!)).IsEquivalentTo(["banking", "labor", "commercial"]);
        await Assert.That(capabilities.GetProperty("timeZone").GetString()).IsEqualTo("America/Argentina/Buenos_Aires");
        await Assert.That(uruguay.Body.GetProperty("capabilities").GetProperty("holidays").GetArrayLength()).IsEqualTo(0);
        await Assert.That(uruguay.Body.GetProperty("capabilities").GetProperty("businessDayPolicies").GetArrayLength()).IsEqualTo(0);
        await Assert.That(uruguay.Body.GetProperty("capabilities").GetProperty("timeZone").ValueKind).IsEqualTo(JsonValueKind.Null);
    }
}
