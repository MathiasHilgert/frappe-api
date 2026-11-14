using Frappe.Geo.Domain.Currencies;

namespace Frappe.Geo.Application.Currencies;

/// <summary>Outbound port: the currencies some country uses today (ISO 4217), retired ones excluded.</summary>
public interface ICurrencyCatalog
{
    /// <summary>Every currency, ordered by code.</summary>
    Task<IReadOnlyList<CurrencyView>> ListAsync(CancellationToken cancellationToken);

    Task<CurrencyView?> FindAsync(CurrencyCode code, CancellationToken cancellationToken);

    /// <summary>The currencies with these codes, in the order of <paramref name="codes"/>; unknown codes are skipped.</summary>
    Task<IReadOnlyList<CurrencyView>> FindAllAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken);
}
