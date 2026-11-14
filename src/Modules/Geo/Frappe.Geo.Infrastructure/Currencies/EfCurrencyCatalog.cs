using Frappe.Geo.Application.Currencies;
using Frappe.Geo.Domain.Currencies;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Geo.Infrastructure.Currencies;

internal sealed class EfCurrencyCatalog(GeoDbContext db) : ICurrencyCatalog
{
    public async Task<IReadOnlyList<CurrencyView>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await Active().OrderBy(c => c.Code).ToListAsync(cancellationToken);
        return rows.Select(ToView).ToArray();
    }

    public async Task<CurrencyView?> FindAsync(CurrencyCode code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        var row = await Active().SingleOrDefaultAsync(c => c.Code == code.Value, cancellationToken);
        return row is null ? null : ToView(row);
    }

    public async Task<IReadOnlyList<CurrencyView>> FindAllAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var rows = await Active().Where(c => codes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, StringComparer.Ordinal, cancellationToken);
        return codes.Where(rows.ContainsKey).Select(code => ToView(rows[code])).ToArray();
    }

    private IQueryable<CurrencyRow> Active() => db.Set<CurrencyRow>().AsNoTracking().Where(c => c.RetiredAt == null);

    private static CurrencyView ToView(CurrencyRow row) =>
        new(row.Code, row.MinorUnitDigits, [.. row.Names.Select(n => new CurrencyName(n.Language, n.Name, n.Symbol))]);
}
