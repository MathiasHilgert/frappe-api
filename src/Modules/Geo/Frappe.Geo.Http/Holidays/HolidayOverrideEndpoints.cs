using Frappe.Geo.Application.Holidays.OverrideObservance;
using Frappe.Geo.Application.Holidays.RemoveObservanceOverride;
using Frappe.Geo.Application.Holidays.SuppressObservance;
using Frappe.Http;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Http.Holidays;

/// <summary>
/// Manual corrections of a country's or subdivision's calendar (S9, S12): they win over the source, also after it
/// changes. A correction needs the jurisdiction's own calendar of that year: it never opens one, because a calendar's
/// existence is what says Geo knows that year's holidays.
/// </summary>
internal static class HolidayOverrideEndpoints
{
    private const string Shape = "The optional scope picks a sector's calendar the country declares (banking) instead of the general one; 400 geo.calendar_scope.invalid for any other. "
        + "A missing date or name is 400 geo.parameter.required; problems of the values all come at once. 404 geo.holidays.not_covered when Geo has no calendar of the jurisdiction itself for the date's year (and scope); 409 when someone else changed the calendar meanwhile (read it again and retry).";

    public static void MapHolidayOverrideEndpoints(this RouteGroupBuilder geo)
    {
#pragma warning disable S1135 // The TODO is tracked: authentication is out of v1's scope (S17).
        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapPut("/countries/{countryCode}/holidays/overrides", OverrideCountryAsync).Overriding("Country", "a country's");

        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapPut("/subdivisions/{subdivisionCode}/holidays/overrides", OverrideSubdivisionAsync).Overriding("Subdivision", "a subdivision's own");

        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapPut("/countries/{countryCode}/holidays/suppressions", SuppressCountryAsync).Suppressing("Country", "a country's");

        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapPut("/subdivisions/{subdivisionCode}/holidays/suppressions", SuppressSubdivisionAsync).Suppressing("Subdivision", "a subdivision's own");

        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapDelete("/countries/{countryCode}/holidays/overrides", RemoveCountryAsync).Removing("Country", "a country's");

        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapDelete("/subdivisions/{subdivisionCode}/holidays/overrides", RemoveSubdivisionAsync).Removing("Subdivision", "a subdivision's own");
#pragma warning restore S1135
    }

    private static void Overriding(this RouteHandlerBuilder endpoint, string place, string owner) => endpoint
        .WithName($"Override{place}Observance")
        .WithSummary($"Set an observance of {owner} calendar by hand")
        .WithDescription("Adds the observance, or replaces the source's of the same date and name; 204 when done. Kind is required too; a community must be one the country declares; legalBasis.url an absolute http(s) address. " + Shape)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    private static void Suppressing(this RouteHandlerBuilder endpoint, string place, string owner) => endpoint
        .WithName($"Suppress{place}Observance")
        .WithSummary($"Leave an observance out of {owner} calendar")
        .WithDescription("Leaves the source's observance of that date and name out, whatever the source says later; 204 when done, 422 geo.observance.not_in_source when the source has no such observance. " + Shape)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static void Removing(this RouteHandlerBuilder endpoint, string place, string owner) => endpoint
        .WithName($"Remove{place}ObservanceOverride")
        .WithSummary($"Remove a manual correction of {owner} calendar")
        .WithDescription("Drops the override (set or suppressed) of date and name, so the source's observance counts again; 404 geo.holiday_override.not_found when there is none. " + Shape)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

    private static Task<Results<NoContent, ErrorsProblemHttpResult>> OverrideCountryAsync(
        string countryCode, ObservanceOverrideRequest request, [FromQuery, DocumentedParameter(Pattern = "^[A-Za-z]{1,16}$")] string? scope, ICommandHandler<OverrideObservance, Unit> handler, CancellationToken cancellationToken) =>
        OverrideAsync(countryCode, request, scope, handler, cancellationToken);

    private static Task<Results<NoContent, ErrorsProblemHttpResult>> OverrideSubdivisionAsync(
        string subdivisionCode, ObservanceOverrideRequest request, [FromQuery, DocumentedParameter(Pattern = "^[A-Za-z]{1,16}$")] string? scope, ICommandHandler<OverrideObservance, Unit> handler, CancellationToken cancellationToken) =>
        OverrideAsync(subdivisionCode, request, scope, handler, cancellationToken);

    private static Task<Results<NoContent, ErrorsProblemHttpResult>> SuppressCountryAsync(
        string countryCode, ObservanceSuppressionRequest request, [FromQuery, DocumentedParameter(Pattern = "^[A-Za-z]{1,16}$")] string? scope, ICommandHandler<SuppressObservance, Unit> handler, CancellationToken cancellationToken) =>
        SuppressAsync(countryCode, request, scope, handler, cancellationToken);

    private static Task<Results<NoContent, ErrorsProblemHttpResult>> SuppressSubdivisionAsync(
        string subdivisionCode, ObservanceSuppressionRequest request, [FromQuery, DocumentedParameter(Pattern = "^[A-Za-z]{1,16}$")] string? scope, ICommandHandler<SuppressObservance, Unit> handler, CancellationToken cancellationToken) =>
        SuppressAsync(subdivisionCode, request, scope, handler, cancellationToken);

    private static Task<Results<NoContent, ErrorsProblemHttpResult>> RemoveCountryAsync(
        string countryCode, [FromQuery, DocumentedParameter(Required = true, Format = ParameterFormat.Date)] string? date,
        [FromQuery, DocumentedParameter(Required = true)] string? name, [FromQuery, DocumentedParameter(Pattern = "^[A-Za-z]{1,16}$")] string? scope, ICommandHandler<RemoveObservanceOverride, Unit> handler, CancellationToken cancellationToken) =>
        RemoveAsync(new RemoveObservanceOverride(countryCode, date, name, scope), handler, cancellationToken);

    private static Task<Results<NoContent, ErrorsProblemHttpResult>> RemoveSubdivisionAsync(
        string subdivisionCode, [FromQuery, DocumentedParameter(Required = true, Format = ParameterFormat.Date)] string? date,
        [FromQuery, DocumentedParameter(Required = true)] string? name, [FromQuery, DocumentedParameter(Pattern = "^[A-Za-z]{1,16}$")] string? scope, ICommandHandler<RemoveObservanceOverride, Unit> handler, CancellationToken cancellationToken) =>
        RemoveAsync(new RemoveObservanceOverride(subdivisionCode, date, name, scope), handler, cancellationToken);

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> OverrideAsync(
        string jurisdiction, ObservanceOverrideRequest request, string? scope, ICommandHandler<OverrideObservance, Unit> handler, CancellationToken cancellationToken) =>
        (await GeoCommands.HandleAsync(
            handler,
            new OverrideObservance(jurisdiction, request.Date, request.Name, request.Kind, request.MovedFrom, request.Community, request.LegalBasis?.Citation, request.LegalBasis?.Url, scope),
            cancellationToken)).ToTypedHttpResult(_ => TypedResults.NoContent());

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> SuppressAsync(
        string jurisdiction, ObservanceSuppressionRequest request, string? scope, ICommandHandler<SuppressObservance, Unit> handler, CancellationToken cancellationToken) =>
        (await GeoCommands.HandleAsync(handler, new SuppressObservance(jurisdiction, request.Date, request.Name, scope), cancellationToken))
            .ToTypedHttpResult(_ => TypedResults.NoContent());

    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> RemoveAsync(
        RemoveObservanceOverride command, ICommandHandler<RemoveObservanceOverride, Unit> handler, CancellationToken cancellationToken) =>
        (await GeoCommands.HandleAsync(handler, command, cancellationToken))
            .ToTypedHttpResult(_ => TypedResults.NoContent());
}
