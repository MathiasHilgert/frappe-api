using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Holidays;

internal sealed class EfHolidayCalendars(GeoDbContext db) : IHolidayCalendars
{
    public Task<HolidayCalendar?> FindAsync(Jurisdiction jurisdiction, HolidayYear year, CalendarScope scope, CancellationToken cancellationToken) =>
        db.Set<HolidayCalendar>().SingleOrDefaultAsync(c => c.Jurisdiction == jurisdiction && c.Year == year && c.Scope == scope, cancellationToken);

    public async Task<IReadOnlyList<(CalendarScope Scope, int Year)>> CoverageAsync(CountryCode country, CancellationToken cancellationToken)
    {
        var jurisdiction = Jurisdiction.Of(country);
        var stored = await db.Set<HolidayCalendar>().AsNoTracking()
            .Where(c => c.Jurisdiction == jurisdiction)
            .Select(c => new { c.Scope, c.Year })
            .ToListAsync(cancellationToken);
        return [.. stored.Select(c => (c.Scope, c.Year.Value))];
    }

    public void Add(HolidayCalendar calendar) => db.Add(calendar);
}
