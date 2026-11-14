using Frappe.Geo.Domain.Countries;

namespace Frappe.Geo.Application.Countries;

/// <summary>Outbound port: the countries Geo knows (ISO 3166-1), retired ones excluded.</summary>
public interface ICountryCatalog
{
    /// <summary>Every country, ordered by code.</summary>
    Task<IReadOnlyList<CountryView>> ListAsync(CancellationToken cancellationToken);

    Task<CountryView?> FindAsync(CountryCode code, CancellationToken cancellationToken);
}
