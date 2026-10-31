using Frappe.Geo.Application.Timezones.ListTimezones;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Timezones.ListTimezones;

/// <summary>Reads the timezone catalogue from the <c>geo</c> schema.</summary>
/// <param name="db">The module's context.</param>
// Public because Wolverine's generated handler code constructs it; internal implementations are unproven there.
public sealed class ListTimezonesQueryService(GeoDbContext db) : IListTimezonesQueryService
{
    // A small catalogue is one page capped at this size; the TZDB lists about 600 ids.
    private const int PageCap = 1000;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimezoneDto>> ListAsync(CancellationToken cancellationToken)
    {
        // One row past the cap tells a full page from a truncated one.
        List<TimezoneDto> zones = await db
            .Timezones.OrderBy(static zone => zone.Name)
            .Take(PageCap + 1)
            .Select(static zone => new TimezoneDto(zone.Name, zone.CanonicalName))
            .ToListAsync(cancellationToken);
        // Rows are never deleted, so past the cap the one page would drop zones while reporting no next page.
        return zones.Count <= PageCap
            ? zones
            : throw new InvalidOperationException(
                $"The timezone catalogue holds more than {PageCap} zones, the single-page cap; page it by cursor."
            );
    }
}
