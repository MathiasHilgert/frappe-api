using Frappe.Geo.Domain.Holidays;
using NodaTime;

namespace Frappe.Geo.Domain.BusinessDays;

/// <summary>
/// The business days of one jurisdiction's year under one policy, computed from its effective calendars: a date is a
/// business day unless it falls on the country's weekend, holds a general observance of a kind the policy treats as
/// non-working, or holds any observance of one of the policy's sector calendars. A sector calendar says whether the
/// sector closes, not what the day is: when the general calendar already names the day (anything but a community day,
/// which applies only to its members), that naming is the one listed and its kind stands, so a day both calendars name
/// is listed once.
/// </summary>
public sealed class BusinessCalendar
{
    private readonly ILookup<LocalDate, Observance> _general;
    private readonly ILookup<LocalDate, Observance> _sector;
    private readonly IReadOnlySet<IsoDayOfWeek> _weekend;
    private readonly PolicyRule _rule;

    private BusinessCalendar(HolidayYear year, IReadOnlySet<IsoDayOfWeek> weekend, PolicyRule rule, IEnumerable<Observance> general, IEnumerable<Observance> sector)
    {
        Year = year;
        _weekend = weekend;
        _rule = rule;
        _general = general.ToLookup(o => o.Date);
        _sector = sector.ToLookup(o => o.Date);
    }

    public HolidayYear Year { get; }

    /// <summary>
    /// The calendar of <paramref name="year"/> under <paramref name="rule"/>, from the effective observances of the
    /// jurisdiction's general calendars (<paramref name="general"/>) and of the rule's sector calendars (<paramref name="sector"/>).
    /// </summary>
    public static BusinessCalendar Of(
        HolidayYear year, IReadOnlySet<IsoDayOfWeek> weekend, PolicyRule rule, IEnumerable<Observance> general, IEnumerable<Observance> sector)
    {
        ArgumentNullException.ThrowIfNull(year);
        ArgumentNullException.ThrowIfNull(weekend);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(general);
        ArgumentNullException.ThrowIfNull(sector);
        return new BusinessCalendar(year, weekend, rule, general, sector);
    }

    /// <summary>What <paramref name="date"/>, a date of <see cref="Year"/>, is under the policy.</summary>
    public BusinessDay DayOf(LocalDate date)
    {
        if (date.Year != Year.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, $"The calendar is of {Year}.");
        }

        var general = _general[date].ToArray();
        var sector = _sector[date].ToArray();
        var weekend = _weekend.Contains(date.DayOfWeek);
        var closed = weekend || sector.Length > 0 || general.Any(o => _rule.NonWorkingKinds.Contains(o.Kind));
        var named = general.Any(o => o.Kind != ObservanceKind.CommunityNonWorkingDay);
        Observance[] observances = [.. general.Concat(named ? [] : sector).OrderBy(o => o.Name, StringComparer.Ordinal)];
        return new BusinessDay(date, !closed, weekend, Observance.StrongestKind(observances), observances);
    }

    /// <summary>The business days from <paramref name="from"/> (included) to the end of <see cref="Year"/>, in order.</summary>
    public IEnumerable<LocalDate> BusinessDaysFrom(LocalDate from)
    {
        var last = new LocalDate(Year.Value, 12, 31);
        var first = from.Year < Year.Value ? new LocalDate(Year.Value, 1, 1) : from;
        for (var date = first; date <= last; date = date.PlusDays(1))
        {
            if (DayOf(date).IsBusinessDay)
            {
                yield return date;
            }
        }
    }
}
