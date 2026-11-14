using Frappe.Geo.Countries.Argentina.Banking;
using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using NodaTime;

namespace Frappe.Geo.Countries.Argentina.Calendar;

/// <summary>
/// Argentina's business-day rules (S8): Saturday and Sunday are the weekend, Buenos Aires the reference time zone, and
/// <list type="bullet">
/// <item><c>banking</c>: holidays, tourist non-working days and non-working days close banks (LCT art. 167), plus every
/// day of the banking calendar (the BCRA's list and the 6 November sector rule, <see cref="BankingHolidaySource"/>);</item>
/// <item><c>labor</c>: only holidays are rest days by law (art. 166); a non-working day is the employer's choice
/// (art. 167), so the answer that matters is the day's kind;</item>
/// <item><c>commercial</c>: Monday to Friday minus holidays; non-working days are ordinary days.</item>
/// </list>
/// A community non-working day (Jewish, Islamic, Armenian holidays) applies only to members, so no policy closes on it;
/// those three are the communities the official calendar marks (<see cref="Communities"/>).
/// </summary>
public static class ArgentineCalendarRules
{
    private static readonly HashSet<ObservanceKind> HolidaysOnly = [ObservanceKind.Holiday];

    /// <summary>The communities of Argentina's community non-working days, as the official calendar marks them (a, b, c).</summary>
    public const string Armenian = "armenian";
    public const string Jewish = "jewish";
    public const string Islamic = "islamic";

    public static IReadOnlySet<string> Communities { get; } = new HashSet<string>(StringComparer.Ordinal) { Armenian, Jewish, Islamic };

    public static CalendarRules Rules { get; } = new(
        CountryCode.Create("AR").Value,
        new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday },
        DateTimeZoneProviders.Tzdb["America/Argentina/Buenos_Aires"],
        new Dictionary<BusinessDayPolicy, PolicyRule>
        {
            [BusinessDayPolicy.Banking] = new(
                new HashSet<ObservanceKind> { ObservanceKind.Holiday, ObservanceKind.TouristNonWorkingDay, ObservanceKind.NonWorkingDay },
                [BankingHolidaySource.Scope]),
            [BusinessDayPolicy.Labor] = new(HolidaysOnly, []),
            [BusinessDayPolicy.Commercial] = new(HolidaysOnly, []),
        },
        Communities);
}
