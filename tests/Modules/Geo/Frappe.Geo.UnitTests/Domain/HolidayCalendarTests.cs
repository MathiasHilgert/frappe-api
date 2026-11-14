using Frappe.Geo.Domain;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using NodaTime;

namespace Frappe.Geo.UnitTests.Domain;

/// <summary>The calendar aggregate, with the shapes of Argentina's 2026 official file.</summary>
public sealed class HolidayCalendarTests
{
    private static readonly Jurisdiction Argentina = Jurisdiction.Of(CountryCode.Create("AR").Value);
    private static readonly HolidayYear Year2026 = HolidayYear.Create(2026).Value;

    private static Observance Holiday(int month, int day, string name, LocalDate? movedFrom = null) =>
        Observance.Create(new LocalDate(2026, month, day), name, ObservanceKind.Holiday, movedFrom).Value;

    private static Observance NonWorking(int month, int day, string name) =>
        Observance.Create(new LocalDate(2026, month, day), name, ObservanceKind.NonWorkingDay).Value;

    private static Observance Community(int month, int day, string name, string community) =>
        Observance.Create(new LocalDate(2026, month, day), name, ObservanceKind.CommunityNonWorkingDay, community: community).Value;

    private static HolidayCalendar Imported(params Observance[] observances)
    {
        var calendar = HolidayCalendar.Open(Argentina, Year2026);
        calendar.Import(observances);
        calendar.DequeueDomainEvents();
        return calendar;
    }

    [Test]
    public async Task One_date_holds_several_observances_and_the_day_takes_the_strongest_kind()
    {
        var calendar = Imported(
            Holiday(4, 2, "Malvinas"), NonWorking(4, 2, "Jueves Santo"), Community(4, 2, "Pesaj", "jewish"), Community(4, 24, "Armenia", "armenian"));

        await Assert.That(calendar.Effective.Count(o => o.Date == new LocalDate(2026, 4, 2))).IsEqualTo(3);
        await Assert.That(calendar.KindOf(new LocalDate(2026, 4, 2))).IsEqualTo(ObservanceKind.Holiday);
        await Assert.That(calendar.KindOf(new LocalDate(2026, 4, 24))).IsEqualTo(ObservanceKind.CommunityNonWorkingDay);
        await Assert.That(calendar.KindOf(new LocalDate(2026, 4, 3))).IsNull();
    }

    [Test]
    public async Task A_moved_holiday_keeps_the_date_it_was_moved_from_and_one_left_on_its_date_did_not_move()
    {
        var guemes = Holiday(6, 15, "Güemes", movedFrom: new LocalDate(2026, 6, 17));
        var sanMartin = Holiday(8, 17, "San Martín", movedFrom: new LocalDate(2026, 8, 17));

        await Assert.That(guemes.MovedFrom).IsEqualTo(new LocalDate(2026, 6, 17));
        await Assert.That(sanMartin.MovedFrom).IsNull();
    }

    [Test]
    public async Task An_observance_outside_the_year_or_twice_under_one_name_refuses_the_whole_import()
    {
        var calendar = HolidayCalendar.Open(Argentina, Year2026);
        var nextNewYear = Observance.Create(new LocalDate(2027, 1, 1), "Año Nuevo", ObservanceKind.Holiday).Value;

        var result = calendar.Import([Holiday(1, 1, "Año Nuevo"), nextNewYear, Holiday(2, 16, "Carnaval"), Holiday(2, 16, "Carnaval")]);

        await Assert.That(result.Errors.Select(e => e.Code)).IsEquivalentTo([GeoErrorCodes.ObservanceDateOutsideYear, GeoErrorCodes.ObservanceDuplicate]);
        await Assert.That(calendar.Effective).IsEmpty();
    }

    [Test]
    public async Task Importing_announces_a_change_only_when_the_effective_calendar_changes()
    {
        var calendar = Imported(Holiday(1, 1, "Año Nuevo"));

        calendar.Import([Holiday(1, 1, "Año Nuevo")]);
        var unchanged = calendar.DequeueDomainEvents();
        calendar.Import([Holiday(1, 1, "Año Nuevo"), Holiday(12, 25, "Navidad")]);
        var changed = calendar.DequeueDomainEvents();

        await Assert.That(unchanged).IsEmpty();
        await Assert.That(changed.OfType<HolidayCalendarChanged>().Single()).IsEqualTo(new HolidayCalendarChanged(Argentina, Year2026));
    }

    [Test]
    public async Task A_calendar_that_appears_is_announced_once_even_without_observances()
    {
        var empty = HolidayCalendar.Open(Argentina, Year2026);
        empty.Import([]);
        var first = HolidayCalendar.Open(Argentina, Year2026);
        first.Import([Holiday(1, 1, "Año Nuevo")]);

        await Assert.That(empty.DomainEvents.OfType<HolidayCalendarChanged>().Single()).IsEqualTo(new HolidayCalendarChanged(Argentina, Year2026));
        await Assert.That(first.DomainEvents.OfType<HolidayCalendarChanged>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task An_empty_calendar_imported_empty_again_announces_nothing()
    {
        var calendar = Imported();

        calendar.Import([]);

        await Assert.That(calendar.DomainEvents).IsEmpty();
    }

    [Test]
    public async Task An_override_wins_over_the_source_and_a_later_source_change_under_it_is_a_conflict_not_a_change()
    {
        var calendar = Imported(NonWorking(4, 2, "Jueves Santo"));
        calendar.Override(Holiday(4, 2, "Jueves Santo"));
        calendar.DequeueDomainEvents();

        calendar.Import([Community(4, 2, "Jueves Santo", "christian")]);
        var events = calendar.DequeueDomainEvents();

        await Assert.That(calendar.KindOf(new LocalDate(2026, 4, 2))).IsEqualTo(ObservanceKind.Holiday);
        await Assert.That(events.OfType<ObservanceOverrideConflicted>().Single().Name).IsEqualTo("Jueves Santo");
        await Assert.That(events.OfType<HolidayCalendarChanged>()).IsEmpty();
    }

    [Test]
    public async Task Every_change_of_the_entries_moves_the_revision_and_a_same_import_does_not()
    {
        var calendar = Imported(Holiday(4, 2, "Jueves Santo"));
        var opened = calendar.Revision;

        calendar.Import([Holiday(4, 2, "Jueves Santo")]);
        var sameImport = calendar.Revision;
        calendar.Override(Holiday(4, 2, "Jueves Santo"));
        var overridden = calendar.Revision;
        calendar.Suppress(new LocalDate(2026, 4, 2), "Jueves Santo");
        var suppressed = calendar.Revision;
        calendar.RemoveOverride(new LocalDate(2026, 4, 2), "Jueves Santo");
        var removed = calendar.Revision;
        calendar.RemoveOverride(new LocalDate(2026, 4, 2), "Jueves Santo");

        await Assert.That(sameImport).IsEqualTo(opened);
        await Assert.That(new[] { overridden, suppressed, removed }).IsEquivalentTo(new[] { opened + 1, opened + 2, opened + 3 });
        await Assert.That(calendar.Revision).IsEqualTo(removed);
    }

    [Test]
    public async Task A_suppressed_observance_stays_out_until_its_override_is_removed()
    {
        var calendar = Imported(Holiday(11, 9, "Visita del Papa"));

        calendar.Suppress(new LocalDate(2026, 11, 9), "Visita del Papa");
        var suppressed = calendar.Effective.Count;
        calendar.RemoveOverride(new LocalDate(2026, 11, 9), "Visita del Papa");

        await Assert.That(suppressed).IsEqualTo(0);
        await Assert.That(calendar.Effective.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(ObservanceKind.Holiday, "jewish")]
    [Arguments(ObservanceKind.CommunityNonWorkingDay, null)]
    public async Task A_community_belongs_exactly_to_a_community_non_working_day(ObservanceKind kind, string? community)
    {
        var result = Observance.Create(new LocalDate(2026, 4, 2), "Pesaj", kind, community: community);

        await Assert.That(result.Errors.Single().Code).IsEqualTo(GeoErrorCodes.ObservanceInvalid);
    }

    [Test]
    [Arguments("2026", null)]
    [Arguments("1899", GeoErrorCodes.HolidayYearOutOfRange)]
    [Arguments("20x6", GeoErrorCodes.HolidayYearInvalid)]
    public async Task A_year_is_digits_from_1900_to_2100_and_a_number_outside_is_out_of_range(string raw, string? code) =>
        await Assert.That(HolidayYear.Parse(raw).Errors.SingleOrDefault()?.Code).IsEqualTo(code);

    private static string[] Reasons(Frappe.Results.Result<Observance> result) =>
        [.. result.Errors.Select(e => (string)e.Parameters["reason"]!)];

    [Test]
    public async Task Every_problem_of_an_observance_is_reported_at_once_as_a_stable_reason_with_an_iso_date()
    {
        var result = Observance.Create(new LocalDate(2026, 4, 2), new string('n', 201), ObservanceKind.Holiday, new LocalDate(2025, 4, 2), "jewish");

        await Assert.That(Reasons(result)).IsEquivalentTo(["name_too_long", "moved_from_not_allowed", "community_not_allowed"]);
        await Assert.That(result.Errors.Select(e => (string)e.Parameters["date"]!).Distinct()).IsEquivalentTo(["2026-04-02"]);
        await Assert.That(result.Errors.Single(e => (string?)e.Parameters["reason"] == "name_too_long").Parameters["max"]).IsEqualTo(Observance.MaxNameLength);
    }

    [Test]
    [Arguments("Ley 1", "not a url", "url_invalid")]
    [Arguments("Ley 1", "ftp://example.org/ley", "url_invalid")]
    [Arguments(" ", "https://example.org/ley", "citation_empty")]
    [Arguments("201", null, "citation_too_long")]
    [Arguments("Ley 1", "501", "url_too_long")]
    public async Task A_legal_basis_cites_within_its_bounds_and_links_an_absolute_web_address(string citation, string? url, string reason)
    {
        var cited = citation == "201" ? new string('c', 201) : citation;
        var address = url == "501" ? "https://example.org/" + new string('u', 481) : url;
        var link = address is null ? null : new Uri(address, UriKind.RelativeOrAbsolute);

        var result = Observance.Create(new LocalDate(2026, 4, 2), "Pesaj", ObservanceKind.Holiday, legalBasis: new LegalBasis(cited, link));

        await Assert.That(Reasons(result)).IsEquivalentTo([reason]);
    }

    [Test]
    public async Task Only_an_observance_the_source_publishes_can_be_suppressed()
    {
        var calendar = Imported(Holiday(1, 1, "Año Nuevo"));
        calendar.Override(Holiday(3, 2, "Añadido"));
        var revision = calendar.Revision;

        var unknown = calendar.Suppress(new LocalDate(2026, 3, 3), "Inexistente");
        var handAdded = calendar.Suppress(new LocalDate(2026, 3, 2), "Añadido");

        await Assert.That(unknown.Errors.Single().Code).IsEqualTo(GeoErrorCodes.ObservanceNotInSource);
        await Assert.That(unknown.Errors.Single().Parameters["date"]).IsEqualTo("2026-03-03");
        await Assert.That(handAdded.Errors.Single().Code).IsEqualTo(GeoErrorCodes.ObservanceNotInSource);
        await Assert.That(calendar.Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task The_calendar_s_own_errors_name_iso_dates()
    {
        var calendar = Imported();

        var outside = calendar.Import([Observance.Create(new LocalDate(2027, 1, 1), "Año Nuevo", ObservanceKind.Holiday).Value]);
        var duplicate = calendar.Import([Holiday(1, 1, "Año Nuevo"), Holiday(1, 1, "Año Nuevo")]);

        await Assert.That(outside.Errors.Single().Parameters["date"]).IsEqualTo("2027-01-01");
        await Assert.That(duplicate.Errors.Single().Parameters["date"]).IsEqualTo("2026-01-01");
    }

    [Test]
    public async Task An_unchanged_import_still_records_the_source_attribution_without_announcing_a_change()
    {
        var calendar = Imported(Holiday(1, 1, "Año Nuevo"));
        var credit = new Attribution("argentina.gob.ar", new Uri("https://www.argentina.gob.ar/"), "CC BY 4.0");

        var result = calendar.Import([Holiday(1, 1, "Año Nuevo")], credit);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(calendar.Attribution).IsEqualTo(credit);
        await Assert.That(calendar.DomainEvents).IsEmpty();
    }
}
