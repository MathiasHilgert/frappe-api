using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Infrastructure.Calendars;

/// <summary>
/// One stored calendar as the shared cache holds it, or the fact that Geo has none (<see cref="Covered"/> false), so a
/// missing calendar is cached as missing and never as an empty one.
/// </summary>
internal sealed record CachedCalendar(
    bool Covered,
    string Jurisdiction,
    string? AttributionName,
    Uri? AttributionUrl,
    string? AttributionLicense,
    IReadOnlyList<CachedObservance> Observances)
{
    public static CachedCalendar Missing(Jurisdiction jurisdiction) => new(false, jurisdiction.Value, null, null, null, []);

    public static CachedCalendar Of(EffectiveCalendar calendar) => new(
        true,
        calendar.Jurisdiction,
        calendar.Attribution?.Name,
        calendar.Attribution?.Url,
        calendar.Attribution?.License,
        [.. calendar.Observances.Select(CachedObservance.Of)]);

    public EffectiveCalendar? ToCalendar()
    {
        if (!Covered)
        {
            return null;
        }

        var attribution = AttributionName is not null && AttributionUrl is not null ? new Attribution(AttributionName, AttributionUrl, AttributionLicense) : null;
        return new EffectiveCalendar(Jurisdiction, attribution, [.. Observances.Select(o => o.ToObservance())]);
    }
}
