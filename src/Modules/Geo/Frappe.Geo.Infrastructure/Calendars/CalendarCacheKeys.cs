using System.Globalization;
using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Infrastructure.Calendars;

/// <summary>
/// The cache key of one stored calendar, (jurisdiction, scope, year), and its two tags: the scoped one, evicted when
/// that calendar changes, and the unscoped one, evicted when an event does not say which scope changed.
/// </summary>
internal static class CalendarCacheKeys
{
    private const string Prefix = "geo:calendar";

    public static string Key(string jurisdiction, string scope, int year) => string.Create(CultureInfo.InvariantCulture, $"{Prefix}:{jurisdiction}:{scope}:{year}");

    public static string Tag(string jurisdiction, int year) => string.Create(CultureInfo.InvariantCulture, $"{Prefix}:{jurisdiction}:{year}");

    public static string ScopedTag(string jurisdiction, string scope, int year) => "tag:" + Key(jurisdiction, scope, year);

    public static string[] Tags(Jurisdiction jurisdiction, CalendarScope scope, HolidayYear year) =>
        [Tag(jurisdiction.Value, year.Value), ScopedTag(jurisdiction.Value, scope.Value, year.Value)];
}
