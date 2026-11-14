using Frappe.Geo.Application.Countries;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Countries;

internal sealed class EfCountryCatalog(GeoDbContext db) : ICountryCatalog
{
    public async Task<IReadOnlyList<CountryView>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await Active().OrderBy(c => c.Code).ToListAsync(cancellationToken);
        return rows.Select(ToView).ToArray();
    }

    public async Task<CountryView?> FindAsync(CountryCode code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        var row = await Active().SingleOrDefaultAsync(c => c.Code == code.Value, cancellationToken);
        return row is null ? null : ToView(row);
    }

    private IQueryable<CountryRow> Active() => db.Set<CountryRow>().AsNoTracking().Where(c => c.RetiredAt == null);

    private static CountryView ToView(CountryRow row) => new(
        row.Code,
        row.Alpha3,
        row.Numeric,
        [.. row.Names.Select(n => new CountryName(n.Language, n.Text))],
        [.. row.Currencies.OrderBy(c => c.Rank).Select(c => c.CurrencyCode)]);
}
