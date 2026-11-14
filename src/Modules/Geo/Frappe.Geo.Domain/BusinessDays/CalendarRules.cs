using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using NodaTime;

namespace Frappe.Geo.Domain.BusinessDays;

/// <summary>
/// A country's calendar rules, declared as data by its package: its weekend days, its reference time zone, what each
/// policy it supports treats as non-working, and the communities its community non-working days apply to. A policy
/// absent from <see cref="Policies"/> is not covered.
/// </summary>
public sealed record CalendarRules(
    CountryCode Country,
    IReadOnlySet<IsoDayOfWeek> Weekend,
    DateTimeZone TimeZone,
    IReadOnlyDictionary<BusinessDayPolicy, PolicyRule> Policies,
    IReadOnlySet<string> Communities)
{
    /// <summary>The rule of <paramref name="policy"/>; <see langword="null"/> when the country does not declare it.</summary>
    public PolicyRule? RuleFor(BusinessDayPolicy policy) => Policies.GetValueOrDefault(policy);

    /// <summary>The calendars the country has: the general one, then each sector calendar one of its policies reads.</summary>
    public IReadOnlyList<CalendarScope> Scopes =>
        [CalendarScope.General, .. Policies.OrderBy(p => p.Key).SelectMany(p => p.Value.SectorScopes).Where(s => s != CalendarScope.General).Distinct()];
}
