using Frappe.Geo.Domain.BusinessDays;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Geo.Application.BusinessDays;

/// <summary>How a country package declares its business-day rules without the core knowing the country.</summary>
public static class CalendarRulesRegistration
{
    /// <summary>Declares <paramref name="rules"/>; every host that answers business-day questions composes them.</summary>
    public static IServiceCollection AddCalendarRules(this IServiceCollection services, CalendarRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        services.AddSingleton(rules);
        services.TryAddSingleton<ICalendarRulesCatalog, RegisteredCalendarRules>();
        return services;
    }
}
