using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.Queries;
using Frappe.Geo.Contracts.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Geo.Application;

/// <summary>Geo's public queries (<see cref="IGeoQueries"/>) and the catalogue of business-day rules they read.</summary>
public static class GeoQueriesRegistration
{
    public static IServiceCollection AddGeoQueries(this IServiceCollection services)
    {
        services.TryAddSingleton<ICalendarRulesCatalog, RegisteredCalendarRules>();
        services.TryAddScoped<IGeoQueries, GeoQueries>();
        return services;
    }
}
