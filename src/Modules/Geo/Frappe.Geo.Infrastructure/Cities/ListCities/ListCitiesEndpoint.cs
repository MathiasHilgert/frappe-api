using Frappe.Geo.Application;
using Frappe.Geo.Application.Cities;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Subdivisions;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Cities;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Subdivisions;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Kernel;
using Frappe.Platform.Http.Paging;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Infrastructure.Cities.ListCities;

internal static class ListCitiesEndpoint
{
    private const string CountryPointer = "/countryCode";
    private const string SubdivisionPointer = "/subdivisionCode";

    private static readonly ErrorOr<Success> _absent = Result.Success;

    public static void Map(RouteGroupBuilder group) =>
        // TODO: GET /cities/{cityId} when a client needs one city by id (branch reads city names through
        // ICityCatalogue in v1)
        group
            .MapGet("/cities", ListAsync)
            .AllowAnonymous()
            .WithName("ListCities")
            .WithSummary(
                "Lists the cities (Georef localities), or autocompletes one from a typed query."
            )
            .WithDescription(
                "Without q, a cursor-paged list in name order. With q, an accent- and case-insensitive locality "
                    + "autocomplete from 2 characters: at most 10 results, ranked exact name, name prefix, a word in "
                    + "the name, words found in the province or country name, then similar spellings (from 3 "
                    + "characters); every word must appear in the locality or its province or country name, such as "
                    + "'pilar cordoba' or 'villa maria cordoba'; no cursor, and limit is ignored. Clients should "
                    + "debounce keystrokes (about 250 ms)."
            )
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<Results<Ok<PageResponse<CityDto>>, ProblemHttpResult>> ListAsync(
        string? countryCode,
        string? subdivisionCode,
        string? q,
        int? limit,
        string? cursor,
        string? locale,
        ICountryReader countries,
        ISubdivisionReader subdivisions,
        ICityReader cities,
        HttpContext context,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<CityQuery> query = Read(countryCode, subdivisionCode, q, cursor, locale)
            .FailIf(
                query =>
                    query.Country is { } country && countries.Find(country, Locale.English) is null,
                UnknownFilter.Of(CountryErrors.NotFound, CountryPointer)
            )
            .FailIf(
                query =>
                    query.Subdivision is { } subdivision
                    && (
                        query.Country is { } country
                            ? !subdivisions.BelongsTo(subdivision, country)
                            : subdivisions.Find(subdivision, Locale.English) is null
                    ),
                UnknownFilter.Of(SubdivisionErrors.NotFound, SubdivisionPointer)
            );
        if (query.IsError)
        {
            return query.Errors.ToProblem(context);
        }

        CityQuery read = query.Value;
        if (read.Terms is { } terms)
        {
            Localized<IReadOnlyList<CityDto>> found = await cities.SearchAsync(
                terms,
                read.Country,
                read.Subdivision,
                read.Locale,
                cancellationToken
            );
            BodyLocale.Served(context, found.Locale);
            return TypedResults.Ok(new PageResponse<CityDto>(found.Value, NextCursor: null));
        }

        return await PageAsync(read, limit, cursor, cities, context, cancellationToken);
    }

    // Every format error at once; a cursor makes no sense next to a search, which has one page.
    private static ErrorOr<CityQuery> Read(
        string? countryCode,
        string? subdivisionCode,
        string? q,
        string? cursor,
        string? locale
    )
    {
        ErrorOr<Locale> requested = BodyLocale.Of(locale);
        ErrorOr<CountryCode>? country = countryCode is null
            ? (ErrorOr<CountryCode>?)null
            : CountryCode.Create(countryCode).WithPointer(CountryPointer);
        ErrorOr<SubdivisionCode>? subdivision = subdivisionCode is null
            ? (ErrorOr<SubdivisionCode>?)null
            : SubdivisionCode.Create(subdivisionCode).WithPointer(SubdivisionPointer);
        ErrorOr<CitySearchTerms>? terms = q is null
            ? (ErrorOr<CitySearchTerms>?)null
            : CitySearchTerms.Create(q).WithPointer("/q");
        ErrorOr<Success> noCursor =
            q is not null && cursor is not null
                ? ((ErrorOr<Success>)CityErrors.QueryInvalid).WithPointer("/cursor")
                : _absent;
        ErrorOr<Success> input = Errors.Collect(
            requested,
            country is { } c ? c : _absent,
            subdivision is { } s ? s : _absent,
            terms is { } t ? t : _absent,
            noCursor
        );
        return input.IsError
            ? input.Errors
            : new CityQuery(country?.Value, subdivision?.Value, terms?.Value, requested.Value);
    }

    private static async Task<Results<Ok<PageResponse<CityDto>>, ProblemHttpResult>> PageAsync(
        CityQuery query,
        int? limit,
        string? cursor,
        ICityReader cities,
        HttpContext context,
        CancellationToken cancellationToken
    )
    {
        // The cursor is bound to the filters, so a page never continues under other ones.
        string scope = $"name:asc|country={query.Country}|subdivision={query.Subdivision}";
        CityKey? after = null;
        if (cursor is not null)
        {
            ErrorOr<IReadOnlyList<string>> keys = CursorCodec.Decode(cursor, scope);
            if (keys.IsError)
            {
                return keys.Errors.ToProblem(context);
            }

            if (keys.Value is not [var searchName, var indecId])
            {
                return ((ErrorOr<Success>)PlatformErrors.InvalidCursor)
                    .WithPointer("/cursor")
                    .Errors.ToProblem(context);
            }

            after = new CityKey(searchName, indecId);
        }

        Localized<CityPage> page = await cities.ListAsync(
            query.Country,
            query.Subdivision,
            after,
            PageLimit.Of(limit),
            query.Locale,
            cancellationToken
        );
        BodyLocale.Served(context, page.Locale);
        string? next = page.Value.Next is { } key
            ? CursorCodec.Encode([key.SearchName, key.IndecId], scope)
            : null;
        return TypedResults.Ok(new PageResponse<CityDto>(page.Value.Items, next));
    }

    private sealed record CityQuery(
        CountryCode? Country,
        SubdivisionCode? Subdivision,
        CitySearchTerms? Terms,
        Locale Locale
    );
}
