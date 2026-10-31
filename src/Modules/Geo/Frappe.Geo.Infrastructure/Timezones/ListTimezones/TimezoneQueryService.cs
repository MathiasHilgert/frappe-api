using Frappe.Geo.Application.Timezones.ListTimezones;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Timezones.ListTimezones;

/// <summary>Reads the timezone catalogue from the <c>geo</c> schema.</summary>
/// <param name="db">The module's context.</param>
// Public because Wolverine's generated handler code constructs it; internal implementations are unproven there.
public sealed class TimezoneQueryService(GeoDbContext db) : IListTimezonesQueryService
{
    // A small catalogue is one page capped at this size; the TZDB lists about 600 ids.
    private const int PageCap = 1000;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimezoneDto>> ListAsync(CancellationToken cancellationToken) =>
        await db
            .Timezones.OrderBy(static zone => zone.Name)
            .Take(PageCap)
            .Select(static zone => new TimezoneDto(zone.Name, zone.CanonicalName))
            .ToListAsync(cancellationToken);
}
