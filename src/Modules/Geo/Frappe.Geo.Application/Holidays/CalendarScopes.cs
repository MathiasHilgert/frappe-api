using Frappe.Geo.Domain.Holidays;
using Frappe.Results;

namespace Frappe.Geo.Application.Holidays;

/// <summary>
/// The scopes a country has: the general calendar. Any other scope is refused as invalid, never
/// answered "not covered", since no calendar of it can ever exist.
/// </summary>
internal static class CalendarScopes
{
    /// <summary>The scope <paramref name="raw"/> names in the country, the general one when absent.</summary>
    public static Result<CalendarScope> Resolve(string? raw, Jurisdiction place)
    {
        if (raw is null)
        {
            return CalendarScope.General;
        }

        IReadOnlyList<CalendarScope> known = [CalendarScope.General];
        return CalendarScope.Create(raw) is { IsSuccess: true } scope && known.Contains(scope.Value)
            ? scope.Value
            : GeoErrors.CalendarScopeInvalid(raw, place.CountryCode, known.Select(s => s.Value));
    }
}
