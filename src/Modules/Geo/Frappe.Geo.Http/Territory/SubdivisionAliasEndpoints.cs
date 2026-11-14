using Frappe.Geo.Application.Territory;
using Frappe.Geo.Application.Territory.AddSubdivisionAlias;
using Frappe.Geo.Application.Territory.ListSubdivisionAliases;
using Frappe.Geo.Application.Territory.RemoveSubdivisionAlias;
using Frappe.Http;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Http.Territory;

/// <summary>
/// A subdivision's aliases (S12): the source's, which the territory sync keeps up to date, and the manual ones, which
/// it never touches. Both are served with the subdivision and searched alike.
/// </summary>
internal static class SubdivisionAliasEndpoints
{
    public static void MapSubdivisionAliasEndpoints(this RouteGroupBuilder geo)
    {
#pragma warning disable S1135 // The TODO is tracked: authentication is out of v1's scope (S17).
        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapGet("/subdivisions/{subdivisionCode}/aliases", ListAsync)
            .WithName("ListSubdivisionAliases")
            .WithSummary("Aliases of a subdivision")
            .WithDescription("Every other name of the subdivision, with its origin: source or manual. Never cached (no-store).")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapPost("/subdivisions/{subdivisionCode}/aliases", AddAsync)
            .WithName("AddSubdivisionAlias")
            .WithSummary("Add an alias to a subdivision")
            .WithDescription("Adds a manual alias, which the territory sync never removes, and answers its absolute path in Location. An alias has letters, digits, spaces and . , ' ’ - ( ) & only, at least one letter or digit (400 geo.subdivision_alias.invalid). 409 geo.subdivision_alias.duplicate when the subdivision is already known by it (its name, its code's suffix or an alias, ignoring case and accents); 409 geo.subdivision_alias.taken when another subdivision of the country is.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapDelete("/subdivisions/{subdivisionCode}/aliases/{alias}", RemoveAsync)
            .WithName("RemoveSubdivisionAlias")
            .WithSummary("Remove a manual alias of a subdivision")
            .WithDescription("Removes a manual alias, named as added or differing only by case and accents; 409 geo.subdivision_alias.imported for one of the source's (they change only with the source), 404 geo.subdivision_alias.not_found for one the subdivision does not have.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
#pragma warning restore S1135
    }

    private static async Task<Results<Ok<SubdivisionAliasListResponse>, ErrorsProblemHttpResult>> ListAsync(
        string subdivisionCode, IQueryHandler<ListSubdivisionAliases, IReadOnlyList<SubdivisionAliasView>> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListSubdivisionAliases(subdivisionCode), cancellationToken))
            .ToTypedHttpResult(list => TypedResults.Ok(new SubdivisionAliasListResponse([.. list.Select(ToResponse)])));

    /// <summary>The Location is the absolute path its removal takes, so a client resolves it the same from any base.</summary>
    private static async Task<Results<Created<SubdivisionAliasResponse>, ErrorsProblemHttpResult>> AddAsync(
        string subdivisionCode, SubdivisionAliasRequest request, ICommandHandler<AddSubdivisionAlias, SubdivisionAliasView> handler,
        LinkGenerator links, HttpContext httpContext, CancellationToken cancellationToken) =>
        (await GeoCommands.HandleAsync(handler, new AddSubdivisionAlias(subdivisionCode, request.Alias), cancellationToken))
            .ToTypedHttpResult(alias => TypedResults.Created(
                links.GetPathByName(httpContext, "RemoveSubdivisionAlias", new { subdivisionCode, alias = alias.Alias }),
                ToResponse(alias)));

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> RemoveAsync(
        string subdivisionCode, string alias, ICommandHandler<RemoveSubdivisionAlias, Unit> handler, CancellationToken cancellationToken) =>
        (await GeoCommands.HandleAsync(handler, new RemoveSubdivisionAlias(subdivisionCode, alias), cancellationToken))
            .ToTypedHttpResult(_ => TypedResults.NoContent());

    private static SubdivisionAliasResponse ToResponse(SubdivisionAliasView alias) => new(alias.Alias, alias.Manual ? "manual" : "source");
}
