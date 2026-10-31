using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Contracts;

/// <summary>Reads the public holiday calendars, seeded per country and year.</summary>
public interface IHolidayCatalogue
{
    /// <summary>
    /// Returns the holidays of one year of a country, national and of every subdivision, by date, national before
    /// subdivision ones, then by name.
    /// </summary>
    /// <param name="country">The country.</param>
    /// <param name="year">The calendar year.</param>
    /// <param name="locale">The locale of the names; the nearest one along its fallback chain when not seeded.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The holidays, <c>geo.country.not_found</c>, or <c>geo.holiday.year_unavailable</c> for a year the catalogue
    /// holds no calendar of.
    /// </returns>
    ValueTask<ErrorOr<IReadOnlyList<GeoHoliday>>> HolidaysFor(
        CountryCode country,
        int year,
        Locale locale,
        CancellationToken cancellationToken
    );
}
