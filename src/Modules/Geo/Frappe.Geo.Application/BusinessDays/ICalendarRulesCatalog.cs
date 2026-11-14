using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.Domain.Countries;

namespace Frappe.Geo.Application.BusinessDays;

/// <summary>The business-day rules the composed country packages declared (<see cref="CalendarRulesRegistration.AddCalendarRules"/>).</summary>
public interface ICalendarRulesCatalog
{
    /// <summary>The country's rules; <see langword="null"/> when no package declared them, so its business days are not covered.</summary>
    CalendarRules? Find(CountryCode country);
}
