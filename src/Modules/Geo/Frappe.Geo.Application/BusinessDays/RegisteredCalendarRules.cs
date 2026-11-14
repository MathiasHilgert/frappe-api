using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.Domain.Countries;

namespace Frappe.Geo.Application.BusinessDays;

/// <summary>The rules registered in the container; a country declared twice keeps its last declaration.</summary>
internal sealed class RegisteredCalendarRules(IEnumerable<CalendarRules> declared) : ICalendarRulesCatalog
{
    private readonly Dictionary<CountryCode, CalendarRules> _rules = declared.GroupBy(r => r.Country).ToDictionary(g => g.Key, g => g.Last());

    public CalendarRules? Find(CountryCode country) => _rules.GetValueOrDefault(country);
}
