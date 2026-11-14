using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.BusinessDays.GetBusinessDay;
using Frappe.Geo.Application.BusinessDays.ListBusinessDays;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Contracts.Queries;
using Frappe.Geo.Http.Holidays;
using Frappe.Http;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NodaTime.Text;

namespace Frappe.Geo.Http.BusinessDays;

/// <summary>Whether a date is a business day, and the next business days, in a country or subdivision under a policy.</summary>
internal static class BusinessDayEndpoints
{
    private const string Policy =
        "policy (required) is banking (settlements and payments), labor (payroll: read kind, the day's strongest observance) "
        + "or commercial (suppliers and deadlines). Each country declares what its policies close: its weekend, the kinds of "
        + "observance and the sector calendars each policy treats as non-working; GET /v1/geo/countries/{code} lists the "
        + "policies it answers and its time zone. 404 geo.holidays.not_covered when Geo lacks a calendar the answer needs, "
        + "geo.business_days.not_covered when the country has no rules for the policy; each answer names the general calendars it read (covers) and credits "
        + "the sources of every calendar read (attributions), as their terms ask. A missing parameter is 400 geo.parameter.required.";

    public static void MapBusinessDayEndpoints(this RouteGroupBuilder geo)
    {
        geo.MapGet("/countries/{countryCode}/calendar/{date}", GetCountryDayAsync)
            .WithName("GetCountryCalendarDay").CacheFor(CacheLifetimes.Calendars)
            .WithSummary("A date in a country's business calendar")
            .WithDescription("Whether a date (YYYY-MM-DD) is a business day in a country under a policy, its kind and its observances. " + Policy)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/subdivisions/{subdivisionCode}/calendar/{date}", GetSubdivisionDayAsync)
            .WithName("GetSubdivisionCalendarDay").CacheFor(CacheLifetimes.Calendars)
            .WithSummary("A date in a subdivision's business calendar")
            .WithDescription("The same for a subdivision: its country's calendars and its own where Geo has them (see covers). " + Policy)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/countries/{countryCode}/business-days", ListCountryBusinessDaysAsync)
            .WithName("ListCountryBusinessDays").CacheFor(CacheLifetimes.Calendars)
            .WithSummary("Next business days in a country")
            .WithDescription(Next + Policy)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/subdivisions/{subdivisionCode}/business-days", ListSubdivisionBusinessDaysAsync)
            .WithName("ListSubdivisionBusinessDays").CacheFor(CacheLifetimes.Calendars)
            .WithSummary("Next business days in a subdivision")
            .WithDescription(Next + Policy)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static string Next =>
        $"The first count (1 to {IGeoQueries.MaxBusinessDays}; 400 geo.business_day_count.invalid when not digits, .out_of_range outside) business days from the date from (YYYY-MM-DD, included); a year on the way that Geo does not cover answers 404, never fewer days. ";

    private static Task<Results<Ok<BusinessDayResponse>, ErrorsProblemHttpResult>> GetCountryDayAsync(
        string countryCode, string date, [FromQuery, DocumentedParameter(Required = true, Values = ["banking", "labor", "commercial"])] string? policy, IQueryHandler<GetBusinessDay, BusinessDayView> handler, CancellationToken cancellationToken) =>
        GetDayAsync(new GetBusinessDay(countryCode, date, policy), handler, cancellationToken);

    private static Task<Results<Ok<BusinessDayResponse>, ErrorsProblemHttpResult>> GetSubdivisionDayAsync(
        string subdivisionCode, string date, [FromQuery, DocumentedParameter(Required = true, Values = ["banking", "labor", "commercial"])] string? policy, IQueryHandler<GetBusinessDay, BusinessDayView> handler, CancellationToken cancellationToken) =>
        GetDayAsync(new GetBusinessDay(subdivisionCode, date, policy), handler, cancellationToken);

    private static Task<Results<Ok<BusinessDaysResponse>, ErrorsProblemHttpResult>> ListCountryBusinessDaysAsync(
        string countryCode, [FromQuery, DocumentedParameter(Required = true, Format = ParameterFormat.Date)] string? from,
        [FromQuery, DocumentedParameter(Required = true, Format = ParameterFormat.WholeNumber, Minimum = 1, Maximum = IGeoQueries.MaxBusinessDays)] string? count,
        [FromQuery, DocumentedParameter(Required = true, Values = ["banking", "labor", "commercial"])] string? policy,
        IQueryHandler<ListBusinessDays, BusinessDaysView> handler, CancellationToken cancellationToken) =>
        ListAsync(new ListBusinessDays(countryCode, from, count, policy), handler, cancellationToken);

    private static Task<Results<Ok<BusinessDaysResponse>, ErrorsProblemHttpResult>> ListSubdivisionBusinessDaysAsync(
        string subdivisionCode, [FromQuery, DocumentedParameter(Required = true, Format = ParameterFormat.Date)] string? from,
        [FromQuery, DocumentedParameter(Required = true, Format = ParameterFormat.WholeNumber, Minimum = 1, Maximum = IGeoQueries.MaxBusinessDays)] string? count,
        [FromQuery, DocumentedParameter(Required = true, Values = ["banking", "labor", "commercial"])] string? policy,
        IQueryHandler<ListBusinessDays, BusinessDaysView> handler, CancellationToken cancellationToken) =>
        ListAsync(new ListBusinessDays(subdivisionCode, from, count, policy), handler, cancellationToken);

    private static async Task<Results<Ok<BusinessDayResponse>, ErrorsProblemHttpResult>> GetDayAsync(
        GetBusinessDay query, IQueryHandler<GetBusinessDay, BusinessDayView> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(query, cancellationToken)).ToTypedHttpResult(view => TypedResults.Ok(new BusinessDayResponse(
            view.Jurisdiction,
            LocalDatePattern.Iso.Format(view.Day.Date),
            BusinessDayPolicies.NameOf(view.Policy),
            view.Day.IsBusinessDay,
            view.Day.IsWeekend,
            view.Day.Kind is { } kind ? ObservanceKinds.NameOf(kind) : null,
            [.. view.Day.Observances.Select(ObservanceResponse.Of)],
            view.Covers,
            [.. view.Attributions.Select(AttributionResponse.Of)])));

    private static async Task<Results<Ok<BusinessDaysResponse>, ErrorsProblemHttpResult>> ListAsync(
        ListBusinessDays query, IQueryHandler<ListBusinessDays, BusinessDaysView> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(query, cancellationToken)).ToTypedHttpResult(view => TypedResults.Ok(new BusinessDaysResponse(
            view.Jurisdiction,
            BusinessDayPolicies.NameOf(view.Policy),
            LocalDatePattern.Iso.Format(view.From),
            [.. view.Dates.Select(LocalDatePattern.Iso.Format)],
            view.Covers,
            [.. view.Attributions.Select(AttributionResponse.Of)])));
}
