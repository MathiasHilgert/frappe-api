using Frappe.Geo.Http.BusinessDays;
using Frappe.Geo.Http.Countries;
using Frappe.Geo.Http.Currencies;
using Frappe.Geo.Http.Holidays;
using Frappe.Geo.Http.Territory;
using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Geo.Http;

/// <summary>Inbound HTTP adapter of Geo: maps every feature's endpoints and registers the texts of Geo's error codes.</summary>
public static class GeoHttp
{
    /// <summary>Registers the texts of Geo's error codes (<c>Resources/Messages/*.json</c>).</summary>
    public static IServiceCollection AddGeoHttp(this IServiceCollection services) =>
        services.AddErrorMessages(typeof(GeoHttp).Assembly);

    /// <summary>Maps Geo's routes under <c>/geo</c> of the given (versioned) group.</summary>
    public static IEndpointRouteBuilder MapGeoEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var geo = routes.MapGroup("/geo").WithTags("Geo").AnswerHead();
        geo.MapCountryEndpoints();
        geo.MapCurrencyEndpoints();
        geo.MapTerritoryEndpoints();
        geo.MapHolidayEndpoints();
        geo.MapBusinessDayEndpoints();

        // Administration answers must show the latest state, so no cache keeps them.
        var administration = geo.MapGroup(string.Empty).NoStore();
        administration.MapHolidayOverrideEndpoints();
        administration.MapHolidaySyncEndpoints();
        administration.MapSubdivisionAliasEndpoints();
        return routes;
    }
}
