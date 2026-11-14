using System.Net;
using System.Text.Json;
using Frappe.Geo.Application;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using Npgsql;
using static Frappe.Geo.IntegrationTests.Admin.AdminRequests;

namespace Frappe.Geo.IntegrationTests.Admin;

/// <summary>Manual corrections of a calendar through the Api (S9, S12): they win over the source, also after it changes.</summary>
[ClassDataSource<ValkeyCache>(Shared = SharedType.PerTestSession)]
public sealed class HolidayOverrideTests(ValkeyCache valkey)
{
    private const string Holidays = "/v1/geo/countries/AR/holidays?year=2026";
    private const string Overrides = "/v1/geo/countries/AR/holidays/overrides";
    private const string File2026 = "holidays-2026-es.json";

    private static JsonElement[] Observances(GeoApi.Answer answer) => [.. answer.Body.GetProperty("observances").EnumerateArray()];

    private static JsonElement On(GeoApi.Answer answer, string date) => Observances(answer).Single(o => o.GetProperty("date").GetString() == date);

    private static string Query(string date, string name) => $"{Overrides}?date={date}&name={Uri.EscapeDataString(name)}";

    private static JsonElement[] Errors(GeoApi.Answer answer) => [.. answer.Body.GetProperty("errors").EnumerateArray()];

    /// <summary>Each error as its code and, for an invalid observance, its stable reason.</summary>
    private static string[] Codes(GeoApi.Answer answer) =>
        [.. Errors(answer).Select(e => e.GetProperty("parameters").TryGetProperty("reason", out var reason) ? $"{e.GetProperty("code").GetString()}:{reason.GetString()}" : e.GetProperty("code").GetString()!)];

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_override_wins_after_the_source_changes_under_it_and_the_run_records_the_conflict(GeoDatabase database)
    {
        var government = new SourceServer("holidays");
        await SourceSync.HolidaysAsync(database, government);
        var guemes = On(await SendAsync(database, valkey, HttpMethod.Get, Holidays), "2026-06-15").GetProperty("name").GetString()!;

        var put = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-06-15", name = guemes, kind = "non_working_day" });
        government.Replace(File2026, json => json.Replace("Martín Miguel de Güemes", "Martín M. de Güemes", StringComparison.Ordinal));
        var (metrics, meter, provider) = SourceSync.Metrics(database, NodaTime.SystemClock.Instance);
        await using var _ = provider;
        using var capture = new MetricCapture(meter);
        await SourceSync.HolidaysAsync(database, government, metrics);

        var after = Observances(await SendAsync(database, valkey, HttpMethod.Get, Holidays)).Single(o => o.GetProperty("name").GetString() == guemes);
        var runs = await SendAsync(database, valkey, HttpMethod.Get, "/v1/geo/holiday-syncs/ar-official-holidays/runs");
        var last = runs.Body.GetProperty("runs")[0];
        await Assert.That(put.Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(after.GetProperty("kind").GetString()).IsEqualTo("non_working_day");
        await Assert.That(after.GetProperty("name").GetString()).IsEqualTo(guemes);
        await Assert.That(runs.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(runs.Body.GetProperty("runs").GetArrayLength()).IsEqualTo(2);
        await Assert.That(last.GetProperty("outcome").GetString()).IsEqualTo("applied");
        await Assert.That(last.GetProperty("conflicts").EnumerateArray().Select(c => c.GetProperty("date").GetString()!)).Contains("2026-06-15");
        await Assert.That(capture.Sum(GeoMetrics.OverrideConflictsMetric, (GeoMetrics.SourceTag, "ar-official-holidays")))
            .IsEqualTo(last.GetProperty("conflicts").GetArrayLength());
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_suppressed_observance_is_left_out_until_its_override_is_removed(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));
        var newYear = On(await SendAsync(database, valkey, HttpMethod.Get, Holidays), "2026-01-01").GetProperty("name").GetString()!;

        var suppress = await SendAsync(database, valkey, HttpMethod.Put, "/v1/geo/countries/AR/holidays/suppressions", new { date = "2026-01-01", name = newYear });
        var announced = await database.QueryAsync("SELECT count(*) FROM geo.outbox_messages WHERE type = 'frappe.geo.holidays_changed.v1'", reader => reader.GetInt64(0));
        var suppressed = Observances(await SendAsync(database, valkey, HttpMethod.Get, Holidays));
        var remove = await SendAsync(database, valkey, HttpMethod.Delete, Query("2026-01-01", newYear));
        var restored = Observances(await SendAsync(database, valkey, HttpMethod.Get, Holidays));
        var again = await SendAsync(database, valkey, HttpMethod.Delete, Query("2026-01-01", newYear));

        await Assert.That(suppress.Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(announced[0]).IsEqualTo(2L).Because("the sync announced the calendar, and the correction announces its change (S10)");
        await Assert.That(suppressed.Any(o => o.GetProperty("date").GetString() == "2026-01-01")).IsFalse();
        await Assert.That(remove.Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(restored.Length).IsEqualTo(32);
        await Assert.That(Outcome(again)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holiday_override.not_found"));
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_year_or_subdivision_without_its_own_calendar_is_refused_rather_than_opened(GeoDatabase database)
    {
        await SourceSync.TerritoryAsync(database, new SourceServer());
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var otherYear = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2030-05-01", name = "Día del Trabajador", kind = "holiday" });
        var subdivision = await SendAsync(database, valkey, HttpMethod.Put, "/v1/geo/subdivisions/AR-X/holidays/overrides", new { date = "2026-07-06", name = "Día de la Provincia", kind = "holiday" });
        var covers = await SendAsync(database, valkey, HttpMethod.Get, "/v1/geo/subdivisions/AR-X/holidays?year=2026");

        await Assert.That(Outcome(otherYear)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holidays.not_covered"));
        await Assert.That(Outcome(subdivision)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holidays.not_covered"));
        await Assert.That(covers.Body.GetProperty("covers").EnumerateArray().Select(c => c.GetString()!)).IsEquivalentTo(["AR"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_unreadable_override_is_refused_naming_what_is_wrong(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var badKind = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "party" });
        var badDate = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-02-30", name = "Día X", kind = "holiday" });
        var everything = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new
        {
            date = "2026-02-30",
            name = "Día X",
            kind = "party",
            movedFrom = "yesterday",
            legalBasis = new { citation = " ", url = "ftp://example.org/ley" },
        });

        await Assert.That(Outcome(badKind)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.observance.invalid"));
        await Assert.That(Outcome(badDate)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.observance.invalid"));
        await Assert.That(everything.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(Codes(everything)).IsEquivalentTo([
            "geo.observance.invalid:date_invalid", "geo.observance.invalid:kind_unknown", "geo.observance.invalid:moved_from_invalid",
            "geo.observance.invalid:citation_empty", "geo.observance.invalid:url_invalid"]);
        await Assert.That(Errors(everything).Select(e => e.GetProperty("parameters").GetProperty("date").GetString()!).Distinct()).IsEquivalentTo(["2026-02-30"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_legal_basis_out_of_bounds_is_a_400_never_a_failure_and_nothing_is_stored(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var relative = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "holiday", legalBasis = new { citation = "Ley 1", url = "not a url" } });
        var ftp = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "holiday", legalBasis = new { citation = "Ley 1", url = "ftp://example.org/ley" } });
        var longCitation = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "holiday", legalBasis = new { citation = new string('x', 201) } });
        var longUrl = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "holiday", legalBasis = new { citation = "Ley 1", url = "https://example.org/" + new string('x', 500) } });
        var noCitation = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "holiday", legalBasis = new { citation = " ", url = "https://example.org/ley" } });
        var accepted = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "holiday", legalBasis = new { citation = "Ley 1", url = "https://example.org/ley" } });
        var stored = On(await SendAsync(database, valkey, HttpMethod.Get, Holidays), "2026-03-02").GetProperty("legalBasis");

        await Assert.That(Codes(relative)).IsEquivalentTo(["geo.observance.invalid:url_invalid"]);
        await Assert.That(Codes(ftp)).IsEquivalentTo(["geo.observance.invalid:url_invalid"]);
        await Assert.That(Codes(longCitation)).IsEquivalentTo(["geo.observance.invalid:citation_too_long"]);
        await Assert.That(Codes(longUrl)).IsEquivalentTo(["geo.observance.invalid:url_too_long"]);
        await Assert.That(Codes(noCitation)).IsEquivalentTo(["geo.observance.invalid:citation_empty"]);
        await Assert.That(new[] { relative.Status, longCitation.Status, noCitation.Status }).IsEquivalentTo([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest]);
        await Assert.That(accepted.Status).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(stored.GetProperty("url").GetString()).IsEqualTo("https://example.org/ley");
        await Assert.That(stored.TryGetProperty("text", out _)).IsFalse();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_community_is_one_the_country_declares(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var martian = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "community_non_working_day", community = "martian" });
        var jewish = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "community_non_working_day", community = "jewish" });

        await Assert.That(Outcome(martian)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.observance.invalid"));
        await Assert.That(Codes(martian)).IsEquivalentTo(["geo.observance.invalid:community_unknown"]);
        await Assert.That(Errors(martian)[0].GetProperty("parameters").GetProperty("communities").EnumerateArray().Select(c => c.GetString()!)).IsEquivalentTo(["armenian", "islamic", "jewish"]);
        await Assert.That(jewish.Status).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task Every_problem_of_an_override_is_answered_at_once_with_iso_dates_and_a_translated_reason(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var several = await GeoApi.SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = new string('n', 201), kind = "holiday", movedFrom = "2026-13-01", community = "jewish" }, "es");
        var spanish = Errors(several).Select(e => e.GetProperty("message").GetString()!).ToArray();

        await Assert.That(several.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(Codes(several)).IsEquivalentTo(["geo.observance.invalid:moved_from_invalid", "geo.observance.invalid:name_too_long", "geo.observance.invalid:community_not_allowed"]);
        await Assert.That(Errors(several).Select(e => e.GetProperty("parameters").GetProperty("date").GetString()!).Distinct()).IsEquivalentTo(["2026-03-02"]);
        await Assert.That(spanish.All(m => m.Contains("2026-03-02", StringComparison.Ordinal))).IsTrue();
        await Assert.That(spanish.Any(m => m.Contains("name", StringComparison.Ordinal) && m.Contains("too long", StringComparison.Ordinal))).IsFalse();
        await Assert.That(spanish.Any(m => m.Contains("200", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task Missing_required_parameters_are_reported_as_required_both_in_the_query_and_in_the_body(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var noName = await SendAsync(database, valkey, HttpMethod.Delete, $"{Overrides}?date=2026-01-01");
        var nothing = await SendAsync(database, valkey, HttpMethod.Delete, Overrides);
        var emptyBody = await SendAsync(database, valkey, HttpMethod.Put, Overrides, new { });
        var noSuppressionName = await SendAsync(database, valkey, HttpMethod.Put, "/v1/geo/countries/AR/holidays/suppressions", new { date = "2026-01-01" });

        static string[] Parameters(GeoApi.Answer answer) =>
            [.. answer.Body.GetProperty("errors").EnumerateArray().Select(e => $"{e.GetProperty("code").GetString()}:{e.GetProperty("parameters").GetProperty("parameter").GetString()}")];
        await Assert.That(noName.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(Parameters(noName)).IsEquivalentTo(["geo.parameter.required:name"]);
        await Assert.That(Parameters(nothing)).IsEquivalentTo(["geo.parameter.required:date", "geo.parameter.required:name"]);
        await Assert.That(Parameters(emptyBody)).IsEquivalentTo(["geo.parameter.required:date", "geo.parameter.required:name", "geo.parameter.required:kind"]);
        await Assert.That(Parameters(noSuppressionName)).IsEquivalentTo(["geo.parameter.required:name"]);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task Only_an_observance_the_source_publishes_can_be_suppressed_and_a_missing_override_names_its_iso_date(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var unknown = await SendAsync(database, valkey, HttpMethod.Put, "/v1/geo/countries/AR/holidays/suppressions", new { date = "2026-03-03", name = "Día que no existe" });
        var observances = Observances(await SendAsync(database, valkey, HttpMethod.Get, Holidays));
        var missing = await GeoApi.SendAsync(database, valkey, HttpMethod.Delete, Query("2026-03-03", "Día que no existe"), acceptLanguage: "es");
        var notFound = Errors(missing)[0];

        await Assert.That(Outcome(unknown)).IsEqualTo((HttpStatusCode.UnprocessableEntity, (string?)"geo.observance.not_in_source"));
        await Assert.That(observances.Length).IsEqualTo(32);
        await Assert.That(Outcome(missing)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holiday_override.not_found"));
        await Assert.That(notFound.GetProperty("parameters").GetProperty("date").GetString()).IsEqualTo("2026-03-03");
        await Assert.That(notFound.GetProperty("message").GetString()).Contains("2026-03-03");
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task An_unknown_scope_is_refused_unless_the_country_declares_it(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        var foo = await SendAsync(database, valkey, HttpMethod.Put, Overrides + "?scope=foo", new { date = "2026-03-02", name = "Día X", kind = "holiday" });
        var banking = await SendAsync(database, valkey, HttpMethod.Put, Overrides + "?scope=banking", new { date = "2026-03-02", name = "Día X", kind = "holiday" });

        await Assert.That(Outcome(foo)).IsEqualTo((HttpStatusCode.BadRequest, (string?)"geo.calendar_scope.invalid"));
        await Assert.That(Outcome(banking)).IsEqualTo((HttpStatusCode.NotFound, (string?)"geo.holidays.not_covered"));
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_change_that_loses_a_race_with_another_writer_answers_409_and_changes_nothing(GeoDatabase database)
    {
        await SourceSync.HolidaysAsync(database, new SourceServer("holidays"));

        // Another writer holds the calendar row changed and uncommitted: the request reads the committed calendar, then waits.
        await using var other = new NpgsqlConnection(database.ConnectionString);
        await other.OpenAsync();
        await using var transaction = await other.BeginTransactionAsync();
        await using (var bump = new NpgsqlCommand("UPDATE geo.holiday_calendars SET revision = revision + 1 WHERE jurisdiction = 'AR' AND year = 2026", other, transaction))
        {
            await bump.ExecuteNonQueryAsync();
        }

        var put = SendAsync(database, valkey, HttpMethod.Put, Overrides, new { date = "2026-03-02", name = "Día X", kind = "holiday" });
        await WaitForALockWaitAsync(database, put);
        await transaction.CommitAsync();
        var answer = await put;

        var observances = Observances(await SendAsync(database, valkey, HttpMethod.Get, Holidays));
        await Assert.That(Outcome(answer)).IsEqualTo((HttpStatusCode.Conflict, (string?)"geo.holiday_calendar.changed_concurrently"));
        await Assert.That(observances.Any(o => o.GetProperty("name").GetString() == "Día X")).IsFalse();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_sync_that_loses_a_race_with_an_administrator_is_recorded_as_failed_and_applies_nothing(GeoDatabase database)
    {
        var government = new SourceServer("holidays");
        await SourceSync.HolidaysAsync(database, government);
        government.Replace(File2026, json => json.Replace("Martín Miguel de Güemes", "Martín M. de Güemes", StringComparison.Ordinal));

        await using var other = new NpgsqlConnection(database.ConnectionString);
        await other.OpenAsync();
        await using var transaction = await other.BeginTransactionAsync();
        await using (var bump = new NpgsqlCommand("UPDATE geo.holiday_calendars SET revision = revision + 1 WHERE jurisdiction = 'AR' AND year = 2026", other, transaction))
        {
            await bump.ExecuteNonQueryAsync();
        }

        var sync = SourceSync.HolidaysAsync(database, government);
        await WaitForALockWaitAsync(database, sync);
        await transaction.CommitAsync();
        var outcome = await sync;

        var runs = await SendAsync(database, valkey, HttpMethod.Get, "/v1/geo/holiday-syncs/ar-official-holidays/runs");
        var names = Observances(await SendAsync(database, valkey, HttpMethod.Get, Holidays)).Select(o => o.GetProperty("name").GetString()!).ToArray();
        await Assert.That(outcome).IsEqualTo(Frappe.Geo.Infrastructure.Sync.SyncOutcome.Failed);
        await Assert.That(runs.Body.GetProperty("runs").EnumerateArray().Select(r => r.GetProperty("outcome").GetString()!)).IsEquivalentTo(["failed", "applied"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(names.Any(n => n.Contains("Martín M. de Güemes", StringComparison.Ordinal))).IsFalse();
    }
}
