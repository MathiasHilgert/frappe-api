using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.Domain.Holidays;
using NodaTime;
using ContractKind = Frappe.Geo.Contracts.Queries.DayKind;
using ContractPolicy = Frappe.Geo.Contracts.Queries.BusinessDayPolicy;

namespace Frappe.Geo.UnitTests.Domain;

/// <summary>The business-day rule over a country's rules as data: weekend, kinds that close the day, sector calendars.</summary>
public sealed class BusinessCalendarTests
{
    private static readonly HolidayYear Year2026 = HolidayYear.Create(2026).Value;
    private static readonly HashSet<IsoDayOfWeek> Weekend = [IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday];
    private static readonly CalendarScope Sector = CalendarScope.Create("banking").Value;

    private static Observance Day(int month, int day, string name, ObservanceKind kind) =>
        Observance.Create(new LocalDate(2026, month, day), name, kind, community: kind == ObservanceKind.CommunityNonWorkingDay ? "jewish" : null).Value;

    private static BusinessCalendar Calendar(PolicyRule rule, params Observance[] sector) => BusinessCalendar.Of(
        Year2026,
        Weekend,
        rule,
        [Day(11, 9, "Papal visit", ObservanceKind.Holiday), Day(3, 23, "Tourism", ObservanceKind.TouristNonWorkingDay), Day(4, 2, "Jewish holiday", ObservanceKind.CommunityNonWorkingDay)],
        sector);

    [Test]
    public async Task A_day_closes_on_the_weekend_on_the_kinds_the_policy_names_and_on_any_sector_observance()
    {
        var banking = Calendar(new PolicyRule(new HashSet<ObservanceKind> { ObservanceKind.Holiday, ObservanceKind.TouristNonWorkingDay }, [Sector]), Day(11, 6, "Bank workers", ObservanceKind.Holiday));
        var commercial = Calendar(new PolicyRule(new HashSet<ObservanceKind> { ObservanceKind.Holiday }, []));

        await Assert.That(banking.DayOf(new LocalDate(2026, 11, 6)).IsBusinessDay).IsFalse();
        await Assert.That(banking.DayOf(new LocalDate(2026, 3, 23)).IsBusinessDay).IsFalse();
        await Assert.That(commercial.DayOf(new LocalDate(2026, 3, 23)).IsBusinessDay).IsTrue();
        await Assert.That(commercial.DayOf(new LocalDate(2026, 4, 2)).IsBusinessDay).IsTrue();
        await Assert.That(commercial.DayOf(new LocalDate(2026, 11, 7))).IsEqualTo(new BusinessDay(new LocalDate(2026, 11, 7), false, true, null, []));
    }

    [Test]
    public async Task A_day_s_kind_is_its_strongest_observance_whatever_the_policy()
    {
        var labor = Calendar(new PolicyRule(new HashSet<ObservanceKind> { ObservanceKind.Holiday }, []));

        var tourism = labor.DayOf(new LocalDate(2026, 3, 23));
        await Assert.That((tourism.IsBusinessDay, tourism.Kind)).IsEqualTo((true, (ObservanceKind?)ObservanceKind.TouristNonWorkingDay));
        await Assert.That(labor.DayOf(new LocalDate(2026, 11, 9)).Kind).IsEqualTo(ObservanceKind.Holiday);
    }

    [Test]
    public async Task A_sector_day_whose_general_observance_was_suppressed_is_listed_as_the_sector_names_it_and_stays_closed()
    {
        // The general calendar's effective observances: the papal visit was suppressed by hand, so it no longer names the day.
        var banking = BusinessCalendar.Of(
            Year2026,
            Weekend,
            new PolicyRule(new HashSet<ObservanceKind> { ObservanceKind.Holiday }, [Sector]),
            [],
            [Day(11, 9, "Feriado bancario", ObservanceKind.Holiday)]);

        var papal = banking.DayOf(new LocalDate(2026, 11, 9));

        await Assert.That(papal.IsBusinessDay).IsFalse();
        await Assert.That(papal.Observances.Select(o => o.Name)).IsEquivalentTo(["Feriado bancario"]);
        await Assert.That(papal.Kind).IsEqualTo(ObservanceKind.Holiday);
    }

    [Test]
    public async Task A_day_both_calendars_name_is_listed_once_with_the_general_kind_and_stays_closed()
    {
        var banking = Calendar(
            new PolicyRule(new HashSet<ObservanceKind> { ObservanceKind.Holiday }, [Sector]),
            Day(3, 23, "Feriado bancario", ObservanceKind.Holiday),
            Day(11, 9, "Papal visit", ObservanceKind.Holiday),
            Day(4, 2, "Bank holiday", ObservanceKind.Holiday));

        var tourism = banking.DayOf(new LocalDate(2026, 3, 23));
        var papal = banking.DayOf(new LocalDate(2026, 11, 9));
        var community = banking.DayOf(new LocalDate(2026, 4, 2));

        await Assert.That((tourism.IsBusinessDay, tourism.Kind)).IsEqualTo((false, (ObservanceKind?)ObservanceKind.TouristNonWorkingDay));
        await Assert.That(tourism.Observances.Select(o => o.Name)).IsEquivalentTo(new[] { "Tourism" });
        await Assert.That(papal.Observances.Count).IsEqualTo(1);
        await Assert.That(papal.IsBusinessDay).IsFalse();
        await Assert.That(community.Observances.Select(o => o.Name)).IsEquivalentTo(new[] { "Bank holiday", "Jewish holiday" });
        await Assert.That((community.IsBusinessDay, community.Kind)).IsEqualTo((false, (ObservanceKind?)ObservanceKind.Holiday));
    }

    [Test]
    public async Task The_business_days_from_a_date_include_it_and_stop_at_the_end_of_the_year()
    {
        var commercial = Calendar(new PolicyRule(new HashSet<ObservanceKind> { ObservanceKind.Holiday }, []));

        await Assert.That(commercial.BusinessDaysFrom(new LocalDate(2026, 11, 6)).Take(2)).IsEquivalentTo([new LocalDate(2026, 11, 6), new LocalDate(2026, 11, 10)]);
        await Assert.That(commercial.BusinessDaysFrom(new LocalDate(2026, 12, 31)).ToArray()).IsEquivalentTo([new LocalDate(2026, 12, 31)]);
        await Assert.That(commercial.BusinessDaysFrom(new LocalDate(2025, 12, 30)).First()).IsEqualTo(new LocalDate(2026, 1, 1));
    }

    [Test]
    public async Task The_contract_enums_mirror_the_domain_ones_value_for_value()
    {
        await Assert.That(Enum.GetValues<BusinessDayPolicy>().Select(p => (p.ToString(), (int)p)))
            .IsEquivalentTo(Enum.GetValues<ContractPolicy>().Select(p => (p.ToString(), (int)p)));
        await Assert.That(Enum.GetValues<ObservanceKind>().Select(k => (k.ToString(), (int)k)))
            .IsEquivalentTo(Enum.GetValues<ContractKind>().Select(k => (k.ToString(), (int)k)));
    }
}
