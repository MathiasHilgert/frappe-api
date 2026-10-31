using System.Globalization;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Subdivisions;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using Frappe.Geo.Domain.Subdivisions;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Kernel;
using Frappe.Platform.Http.Paging;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Holidays.ListHolidays;

internal static class ListHolidaysEndpoint
{
    private const int LastYear = 9999;
    private const string YearPointer = "/year";
    private const string SubdivisionPointer = "/subdivisionCode";

    private static readonly IErrorOr _absent = (ErrorOr<Success>)Result.Success;

    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet("/countries/{countryCode}/holidays", List)
            .AllowAnonymous()
            .WithName("ListHolidays")
            .WithSummary(
                "Lists the public holidays of a country for one year (the current year in the country when absent): "
                    + "the national ones, plus those of one subdivision when asked."
            )
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static Results<Ok<PageResponse<HolidayDto>>, ProblemHttpResult> List(
        string countryCode,
        string? year,
        string? subdivisionCode,
        string? locale,
        IHolidayReader holidays,
        ICountryReader countries,
        ISubdivisionReader subdivisions,
        IClock clock,
        HttpContext context
    )
    {
        ErrorOr<Locale> requested = BodyLocale.Of(locale);
        ErrorOr<int>? asked = year is null ? (ErrorOr<int>?)null : Year(year);
        ErrorOr<SubdivisionCode>? subdivision = subdivisionCode is null
            ? (ErrorOr<SubdivisionCode>?)null
            : SubdivisionCode.Create(subdivisionCode).WithPointer(SubdivisionPointer);
        ErrorOr<Success> valid = Errors.Collect(
            requested,
            (IErrorOr?)asked ?? _absent,
            (IErrorOr?)subdivision ?? _absent
        );
        if (valid.IsError)
        {
            return valid.Errors.ToProblem(context);
        }

        // A malformed code in the path names no resource, so it is not found like an unknown one.
        ErrorOr<CountryCode> code = CountryCode.Create(countryCode);
        if (code.IsError || countries.Find(code.Value, Locale.English) is null)
        {
            return new[] { CountryErrors.NotFound }.ToProblem(context);
        }

        // A subdivision of another country is as unknown to this country's calendar as one never catalogued.
        if (
            subdivision is { } filter
            && (
                filter.Value.Country != code.Value
                || subdivisions.Find(filter.Value, Locale.English) is null
            )
        )
        {
            return new[]
            {
                UnknownFilter.Of(SubdivisionErrors.NotFound, SubdivisionPointer),
            }.ToProblem(context);
        }

        int effective = asked?.Value ?? DefaultYear(code.Value, clock, context);
        ErrorOr<Localized<IReadOnlyList<HolidayDto>>> list = holidays.List(
            code.Value,
            effective,
            subdivision?.Value,
            requested.Value
        );
        if (list.IsError)
        {
            return list.Errors.ToProblem(context);
        }

        BodyLocale.Served(context, list.Value.Locale);
        return TypedResults.Ok(new PageResponse<HolidayDto>(list.Value.Value, NextCursor: null));
    }

    // Resolved once per request; the caching filter reads the same year to tag the response and cap its max-age.
    private static int DefaultYear(CountryCode country, IClock clock, HttpContext context)
    {
        (int year, int maxAge) = HolidayYear.Current(country, clock.GetCurrentInstant());
        CacheScope.Set(
            context,
            new CacheScope(year.ToString(CultureInfo.InvariantCulture), maxAge)
        );
        return year;
    }

    private static ErrorOr<int> Year(string year)
    {
        ErrorOr<int> parsed =
            int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value is >= 1 and <= LastYear
                ? value
                : HolidayErrors.YearInvalid;
        return parsed.WithPointer(YearPointer);
    }
}
