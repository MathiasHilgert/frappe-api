using Frappe.Geo.Application.Territory;
using Frappe.Geo.Application.Territory.GetArea;
using Frappe.Geo.Application.Territory.GetLocality;
using Frappe.Geo.Application.Territory.GetSubdivision;
using Frappe.Geo.Application.Territory.ListAreas;
using Frappe.Geo.Application.Territory.ListLocalities;
using Frappe.Geo.Application.Territory.ListSubdivisions;
using Frappe.Http;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Http.Territory;

/// <summary>
/// A country's territory as levels, never as one country's terms: subdivisions (first level, ISO 3166-2), administrative
/// areas (level 2 and below, with the source's category) and localities (populated places).
/// </summary>
internal static class TerritoryEndpoints
{
    private const string ProperNouns = "Names and categories are served as the source writes them, so no Content-Language is declared.";

    private const string UnknownSubdivision =
        "404 geo.territory.not_covered for a subdivision of a country Geo holds no territory of, geo.subdivision.not_found for one Geo does not know. ";

    public static void MapTerritoryEndpoints(this RouteGroupBuilder geo)
    {
        geo.MapGet("/countries/{countryCode}/subdivisions", ListSubdivisionsAsync)
            .WithName("ListSubdivisions").CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("Subdivisions of a country")
            .WithDescription("A country's first-level subdivisions, ordered by ISO 3166-2 code; 404 geo.territory.not_covered for a country Geo holds no territory of (no data, never an empty list), geo.country.not_found for an unknown one. " + ProperNouns)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/subdivisions/{subdivisionCode}", GetSubdivisionAsync)
            .WithName("GetSubdivision").CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("One subdivision")
            .WithDescription("A subdivision by its ISO 3166-2 code (case-insensitive), with the other names it is known by. " + UnknownSubdivision + ProperNouns)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/subdivisions/{subdivisionCode}/areas", ListAreasAsync)
            .WithName("ListAreas").CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("Administrative areas of a subdivision")
            .WithDescription("A subdivision's areas of every level, or of one level (2 to 9), ordered by level then name. " + UnknownSubdivision + ProperNouns)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/areas/{areaId}", GetAreaAsync)
            .WithName("GetArea").CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("One administrative area")
            .WithDescription("An area by its id (country code and the source's id, such as AR:30015); a retired one still answers. " + ProperNouns)
            .ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/subdivisions/{subdivisionCode}/localities", ListLocalitiesAsync)
            .WithName("ListLocalities").CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("Localities of a subdivision")
            .WithDescription("A page of a subdivision's localities, by name; with q, the names containing it first, then the most similar (typos and accents tolerated). "
                + "limit is 1 to 200 (default 50); pass nextCursor as cursor for the next page, with the same q. " + UnknownSubdivision + ProperNouns)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        geo.MapGet("/localities/{localityId}", GetLocalityAsync)
            .WithName("GetLocality").CacheFor(CacheLifetimes.ReferenceData)
            .WithSummary("One locality")
            .WithDescription("A locality by its id (country code and the source's id, such as AR:14014010); a retired one still answers. " + ProperNouns)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<SubdivisionListResponse>, ErrorsProblemHttpResult>> ListSubdivisionsAsync(
        string countryCode, IQueryHandler<ListSubdivisions, IReadOnlyList<SubdivisionView>> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListSubdivisions(countryCode), cancellationToken))
            .ToTypedHttpResult(list => TypedResults.Ok(new SubdivisionListResponse([.. list.Select(ToResponse)])));

    private static async Task<Results<Ok<SubdivisionResponse>, ErrorsProblemHttpResult>> GetSubdivisionAsync(
        string subdivisionCode, IQueryHandler<GetSubdivision, SubdivisionView> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetSubdivision(subdivisionCode), cancellationToken)).ToTypedHttpResult(s => TypedResults.Ok(ToResponse(s)));

    private static async Task<Results<Ok<AreaListResponse>, ErrorsProblemHttpResult>> ListAreasAsync(
        string subdivisionCode, [FromQuery, DocumentedParameter(Format = ParameterFormat.WholeNumber, Minimum = 2)] string? level, IQueryHandler<ListAreas, IReadOnlyList<AdministrativeAreaView>> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListAreas(subdivisionCode, level), cancellationToken))
            .ToTypedHttpResult(list => TypedResults.Ok(new AreaListResponse([.. list.Select(ToResponse)])));

    private static async Task<Results<Ok<AreaResponse>, ErrorsProblemHttpResult>> GetAreaAsync(
        string areaId, IQueryHandler<GetArea, AdministrativeAreaView> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetArea(areaId), cancellationToken)).ToTypedHttpResult(a => TypedResults.Ok(ToResponse(a)));

    private static async Task<Results<Ok<LocalityPageResponse>, ErrorsProblemHttpResult>> ListLocalitiesAsync(
        string subdivisionCode, [FromQuery] string? q, [FromQuery, DocumentedParameter(Format = ParameterFormat.WholeNumber, Minimum = 1)] string? limit, [FromQuery] string? cursor,
        IQueryHandler<ListLocalities, LocalityPage> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListLocalities(subdivisionCode, q, limit, cursor), cancellationToken))
            .ToTypedHttpResult(page => TypedResults.Ok(new LocalityPageResponse([.. page.Localities.Select(ToResponse)], page.NextCursor)));

    private static async Task<Results<Ok<LocalityResponse>, ErrorsProblemHttpResult>> GetLocalityAsync(
        string localityId, IQueryHandler<GetLocality, LocalityView> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetLocality(localityId), cancellationToken)).ToTypedHttpResult(l => TypedResults.Ok(ToResponse(l)));

    private static CentroidResponse? ToResponse(Coordinates? point) => point is null ? null : new(point.Latitude, point.Longitude);

    private static SubdivisionResponse ToResponse(SubdivisionView s) => new(s.Code, s.CountryCode, s.Name, s.Category, ToResponse(s.Centroid), s.Aliases);

    private static AreaResponse ToResponse(AdministrativeAreaView a) => new(a.Id, a.SubdivisionCode, a.Level, a.Category, a.Name, a.IsLocalGovernment, ToResponse(a.Centroid));

    private static LocalityResponse ToResponse(LocalityView l) => new(l.Id, l.SubdivisionCode, l.Name, l.Category, l.AreaId, l.LocalGovernmentId, ToResponse(l.Centroid));
}
