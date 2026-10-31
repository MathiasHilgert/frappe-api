using System.Collections.Frozen;
using Frappe.Geo.Application.TimeZones;
using Frappe.Geo.Contracts.ValueObjects;
using NodaTime.TimeZones;

namespace Frappe.Geo.Infrastructure.TimeZones;

/// <summary>
/// The time zones a branch can pick, from the tzdb NodaTime ships: every zone of zone.tab with its country, plus
/// Etc/UTC with none, by id and exactly as tzdb names them.
/// </summary>
internal sealed class TimeZoneSnapshot : ITimeZoneReader
{
    private const string Utc = "Etc/UTC";

    private readonly TimeZoneDto[] _zones;
    private readonly FrozenDictionary<string, TimeZoneDto[]> _byCountry;

    public TimeZoneSnapshot()
    {
        _zones =
        [
            .. (TzdbDateTimeZoneSource.Default.ZoneLocations ?? [])
                .Select(static location => new TimeZoneDto(
                    TimeZoneId.Create(location.ZoneId).Value,
                    CountryCode.Create(location.CountryCode).Value
                ))
                .Append(new TimeZoneDto(TimeZoneId.Create(Utc).Value, CountryCode: null))
                .OrderBy(static zone => zone.TimeZoneId.Value, StringComparer.Ordinal),
        ];
        _byCountry = _zones
            .Where(static zone => zone.CountryCode is not null)
            .GroupBy(static zone => zone.CountryCode?.Value ?? "", StringComparer.Ordinal)
            .ToFrozenDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                StringComparer.Ordinal
            );
    }

    public IReadOnlyList<TimeZoneDto> List(CountryCode? country) =>
        country is null ? _zones
        : _byCountry.TryGetValue(country.Value, out TimeZoneDto[]? zones) ? zones
        : [];
}
