using System.Collections.Frozen;
using Frappe.Geo.Contracts.ValueObjects;
using NodaTime;
using NodaTime.TimeZones;

namespace Frappe.Geo.Infrastructure.Holidays;

// The year a holiday read without one means: the current year where the country is, so its calendar turns over at
// the country's own new year.
internal static class HolidayYear
{
    private const int MaxCacheSeconds = 86_400;

    // The first zone.tab zone of each country, such as America/Argentina/Buenos_Aires for AR.
    private static readonly FrozenDictionary<string, DateTimeZone> _zones = (
        TzdbDateTimeZoneSource.Default.ZoneLocations ?? []
    )
        .DistinctBy(static location => location.CountryCode, StringComparer.Ordinal)
        .ToFrozenDictionary(
            static location => location.CountryCode,
            static location => DateTimeZoneProviders.Tzdb[location.ZoneId],
            StringComparer.Ordinal
        );

    /// <summary>Returns the zone of a country: its first zone.tab zone, else UTC.</summary>
    public static DateTimeZone ZoneOf(CountryCode country) =>
        _zones.GetValueOrDefault(country.Value) ?? DateTimeZone.Utc;

    /// <summary>
    /// Returns the current year in the country and how many seconds a response for it may be cached: a day at most,
    /// and never past the country's next new year.
    /// </summary>
    public static (int Year, int MaxAge) Current(CountryCode country, Instant now)
    {
        DateTimeZone zone = ZoneOf(country);
        int year = now.InZone(zone).Year;
        Duration left = zone.AtStartOfDay(new LocalDate(year + 1, 1, 1)).ToInstant() - now;
        return (year, (int)Math.Min(MaxCacheSeconds, Math.Floor(left.TotalSeconds)));
    }
}
