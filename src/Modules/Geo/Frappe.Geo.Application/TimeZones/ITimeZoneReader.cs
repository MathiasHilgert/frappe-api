using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.TimeZones;

/// <summary>Reads the time zones a branch can pick.</summary>
public interface ITimeZoneReader
{
    /// <summary>Lists the zones by id.</summary>
    /// <param name="country">Only the zones of this country, or every zone when <see langword="null"/>.</param>
    /// <returns>The zones.</returns>
    IReadOnlyList<TimeZoneDto> List(CountryCode? country);
}
