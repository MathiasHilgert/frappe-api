using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.Holidays;

/// <summary>
/// The calendars of one scope that apply in a jurisdiction in a year, merged: the country's, then the subdivision's
/// own when Geo has it. <see cref="Covers"/> names them; the country's is required, so without it there is no answer
/// (S6: not covered, never "no holidays") and the calendar missing is the country's (<see cref="Jurisdiction.Country"/>).
/// </summary>
internal sealed record CoveredCalendar(IReadOnlyList<string> Covers, IReadOnlyList<Attribution> Attributions, IReadOnlyList<Observance> Observances)
{
    public static async Task<CoveredCalendar?> LoadAsync(
        IEffectiveCalendars calendars, Jurisdiction place, HolidayYear year, CalendarScope scope, CancellationToken cancellationToken)
    {
        if (await calendars.FindAsync(place.Country, year, scope, cancellationToken) is not { } national)
        {
            return null;
        }

        EffectiveCalendar[] covered = place.IsCountry || await calendars.FindAsync(place, year, scope, cancellationToken) is not { } own
            ? [national]
            : [national, own];
        return new CoveredCalendar(
            [.. covered.Select(c => c.Jurisdiction)],
            [.. covered.Select(c => c.Attribution).OfType<Attribution>().Distinct()],
            [.. covered.SelectMany(c => c.Observances).OrderBy(o => o.Date).ThenBy(o => o.Name, StringComparer.Ordinal)]);
    }
}
