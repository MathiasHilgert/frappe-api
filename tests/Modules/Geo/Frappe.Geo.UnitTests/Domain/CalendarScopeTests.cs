using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using NodaTime;

namespace Frappe.Geo.UnitTests.Domain;

/// <summary>A calendar's scope: the general calendar by default, or a sector's (banking) kept apart under the same jurisdiction and year.</summary>
public sealed class CalendarScopeTests
{
    private static readonly Jurisdiction Argentina = Jurisdiction.Of(CountryCode.Create("AR").Value);
    private static readonly HolidayYear Year2026 = HolidayYear.Create(2026).Value;

    [Test]
    public async Task A_calendar_opened_without_a_scope_is_the_general_one()
    {
        await Assert.That(HolidayCalendar.Open(Argentina, Year2026).Scope).IsEqualTo(CalendarScope.General);
        await Assert.That(CalendarScope.General.Value).IsEqualTo("general");
    }

    [Test]
    public async Task A_scope_is_a_short_lower_case_ascii_word()
    {
        await Assert.That(CalendarScope.Create(" Banking ").Value.Value).IsEqualTo("banking");
        await Assert.That(CalendarScope.Create("").IsSuccess).IsFalse();
        await Assert.That(CalendarScope.Create("bank-ing").IsSuccess).IsFalse();
        await Assert.That(CalendarScope.Create("bancário").IsSuccess).IsFalse();
        await Assert.That(CalendarScope.Create(new string('a', CalendarScope.MaxLength + 1)).IsSuccess).IsFalse();
    }

    [Test]
    public async Task A_change_of_a_sector_calendar_is_announced_with_its_scope()
    {
        var banking = CalendarScope.Create("banking").Value;
        var calendar = HolidayCalendar.Open(Argentina, Year2026, banking);

        calendar.Import([Observance.Create(new LocalDate(2026, 11, 6), "Día del Bancario", ObservanceKind.Holiday).Value]);

        await Assert.That(calendar.Scope).IsEqualTo(banking);
        await Assert.That(calendar.DomainEvents.OfType<HolidayCalendarChanged>().Single().Scope).IsEqualTo(banking);
    }
}
