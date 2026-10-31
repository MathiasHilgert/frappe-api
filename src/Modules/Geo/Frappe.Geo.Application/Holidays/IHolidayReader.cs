using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Holidays;

/// <summary>Reads the public holiday calendars.</summary>
public interface IHolidayReader
{
    /// <summary>
    /// Lists the holidays of one year of a country: the national ones, plus those of <paramref name="subdivision"/>
    /// when given; by date, national before subdivision ones, then by name in <paramref name="locale"/>.
    /// </summary>
    /// <param name="country">The country, already known to the catalogue.</param>
    /// <param name="year">The calendar year.</param>
    /// <param name="subdivision">The subdivision whose holidays are added, or <see langword="null"/>.</param>
    /// <param name="locale">The requested locale of the names.</param>
    /// <returns>The holidays with the locale their names resolved to, or <c>geo.holiday.year_unavailable</c>.</returns>
    ErrorOr<Localized<IReadOnlyList<HolidayDto>>> List(
        CountryCode country,
        int year,
        SubdivisionCode? subdivision,
        Locale locale
    );
}
