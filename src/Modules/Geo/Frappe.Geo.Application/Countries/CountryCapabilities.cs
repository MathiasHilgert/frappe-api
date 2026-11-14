using Frappe.Geo.Domain.BusinessDays;

namespace Frappe.Geo.Application.Countries;

/// <summary>
/// What Geo covers for a country, derived from what it holds and what the country's package declared: its holiday
/// calendars per scope and year, the business-day policies it answers and its reference time zone (none without rules).
/// </summary>
public sealed record CountryCapabilities(IReadOnlyList<HolidayCoverage> Holidays, IReadOnlyList<BusinessDayPolicy> BusinessDayPolicies, string? TimeZone)
{
    internal static CountryCapabilities Of(IEnumerable<(string Scope, int Year)> calendars, CalendarRules? rules) => new(
        [.. calendars.GroupBy(c => c.Scope, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new HolidayCoverage(g.Key, [.. g.Select(c => c.Year).Distinct().Order()]))],
        rules is null ? [] : [.. rules.Policies.Keys.Order()],
        rules?.TimeZone.Id);
}
