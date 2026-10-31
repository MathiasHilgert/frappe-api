using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Contracts;

/// <summary>Reads the city catalogue (the Georef localities), retired cities included.</summary>
public interface ICityCatalogue
{
    /// <summary>Returns one city.</summary>
    /// <param name="city">The city.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The city, whose <see cref="GeoCity.Status"/> says whether it is retired, or <c>geo.city.not_found</c>.</returns>
    ValueTask<ErrorOr<GeoCity>> GetCityAsync(CityId city, CancellationToken cancellationToken);
}
