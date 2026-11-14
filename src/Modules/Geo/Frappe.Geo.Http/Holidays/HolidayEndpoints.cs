using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Holidays.GetHolidays;
using Frappe.Http;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Http.Holidays;

/// <summary>The holidays of a country, or of a subdivision with its country's, in a year.</summary>
internal static class HolidayEndpoints
{
    private const string Shape =
        "year is 1900 to 2100, the current one by default (400 geo.holiday_year.invalid when not digits, .out_of_range outside). scope is general by default, "
        + "or a sector's calendar the country declares (banking: the source's facts as published, credited in attributions); 400 geo.calendar_scope.invalid for any other. "
        + "404 geo.holidays.not_covered names the calendar actually missing (the country's for a subdivision without it). Names are served as the source writes them, so no Content-Language is declared.";

    public static void MapHolidayEndpoints(this RouteGroupBuilder geo)
    {
        geo.MapGet("/countries/{countryCode}/holidays", GetCountryHolidaysAsync)
            .WithName("GetCountryHolidays").CacheFor(CacheLifetimes.Calendars)
            .WithSummary("Holidays of a country")
            .WithDescription("A country's national observances in a year, by date; 404 when Geo has no calendar of it for that year. " + Shape)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/subdivisions/{subdivisionCode}/holidays", GetSubdivisionHolidaysAsync)
            .WithName("GetSubdivisionHolidays").CacheFor(CacheLifetimes.Calendars)
            .WithSummary("Holidays of a subdivision")
            .WithDescription("The observances in a subdivision in a year: its country's, and its own where Geo has them (see covers). " + Shape)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Task<Results<Ok<HolidayCalendarResponse>, ErrorsProblemHttpResult>> GetCountryHolidaysAsync(
        string countryCode, [FromQuery, DocumentedParameter(Format = ParameterFormat.WholeNumber, Minimum = Domain.Holidays.HolidayYear.Min, Maximum = Domain.Holidays.HolidayYear.Max)] string? year, [FromQuery] string? scope, IQueryHandler<GetHolidays, HolidayCalendarView> handler, CancellationToken cancellationToken) =>
        GetAsync(new GetHolidays(countryCode, year, scope), handler, cancellationToken);

    private static Task<Results<Ok<HolidayCalendarResponse>, ErrorsProblemHttpResult>> GetSubdivisionHolidaysAsync(
        string subdivisionCode, [FromQuery, DocumentedParameter(Format = ParameterFormat.WholeNumber, Minimum = Domain.Holidays.HolidayYear.Min, Maximum = Domain.Holidays.HolidayYear.Max)] string? year, [FromQuery] string? scope, IQueryHandler<GetHolidays, HolidayCalendarView> handler, CancellationToken cancellationToken) =>
        GetAsync(new GetHolidays(subdivisionCode, year, scope), handler, cancellationToken);

    private static async Task<Results<Ok<HolidayCalendarResponse>, ErrorsProblemHttpResult>> GetAsync(
        GetHolidays query, IQueryHandler<GetHolidays, HolidayCalendarView> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(query, cancellationToken)).ToTypedHttpResult(view => TypedResults.Ok(ToResponse(view)));

    private static HolidayCalendarResponse ToResponse(HolidayCalendarView view) =>
        new(view.Jurisdiction, view.Year, view.Scope, view.Covers, [.. view.Attributions.Select(AttributionResponse.Of)], [.. view.Observances.Select(ObservanceResponse.Of)]);
}
