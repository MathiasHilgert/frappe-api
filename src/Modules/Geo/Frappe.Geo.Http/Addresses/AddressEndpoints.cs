using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Application.Addresses.GetAddressSuggestions;
using Frappe.Geo.Application.Addresses.ResolveAddress;
using Frappe.Http;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Http.Addresses;

/// <summary>
/// Address suggestions while the user types and the chosen one validated and normalized (S11). The provider never shows:
/// ids are opaque, and a chosen one travels in a request body, never in a URL that logs and traces record (S15). Answers are personal and per session, so no cache may keep them (<see cref="HttpCaching.NoStore{TBuilder}"/>, S12).
/// </summary>
internal static class AddressEndpoints
{
    private const string Session =
        "session is a UUID the client generates when the user starts typing, sends with every suggestion request and with the validation, which closes it; the next search needs a new one. ";

    private const string Unavailable =
        "A malformed request is 400 whether or not a provider is configured; once it is valid, 503 geo.addresses.not_configured "
        + "when this server has no address provider, geo.addresses.unavailable when the provider does not answer.";

    public static void MapAddressEndpoints(this RouteGroupBuilder geo)
    {
#pragma warning disable S1135 // The TODO is tracked: authentication is out of v1's scope (S17).
        // TODO(auth): signed-in users only, rate limited per user; open while Frappé has no authentication (S17).
        geo.MapGet("/address-suggestions", SuggestAsync)
            .WithName("GetAddressSuggestions").NoStore()
            .WithSummary("Address suggestions")
            .WithDescription("Addresses matching q (1 to 200 characters), best first, optionally within one country (ISO 3166-1 alpha-2) and in one language "
                + "(BCP 47, else Accept-Language). " + Session + Unavailable)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // TODO(auth): signed-in users only, rate limited per user; open while Frappé has no authentication (S17).
        geo.MapPost("/address-validations", ValidateAsync)
            .WithName("ValidateAddress").NoStore()
            .WithSummary("Validate an address")
            .WithDescription("The address of a suggestion's id (or of a stored reference), validated and normalized: street, number, postal code, locality, "
                + "coordinates and ISO country and subdivision codes. It is the snapshot to store; Geo keeps nothing. The reference travels in the body, "
                + "never in the URL, so no access log or trace records it. " + Session
                + "400 geo.address_reference.invalid for a reference Geo did not issue, 404 geo.address.not_found when the provider no longer knows it, " + Unavailable)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
#pragma warning restore S1135
    }

    private static async Task<Results<Ok<AddressSuggestionListResponse>, ErrorsProblemHttpResult>> SuggestAsync(
        [FromQuery, DocumentedParameter(Required = true)] string? q,
        [FromQuery, DocumentedParameter(Required = true, Format = ParameterFormat.Uuid)] string? session,
        [FromQuery, DocumentedParameter(Pattern = "^[A-Za-z]{2}$")] string? country,
        [FromQuery] string? language,
        IQueryHandler<GetAddressSuggestions, IReadOnlyList<AddressSuggestion>> handler, HttpContext httpContext, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetAddressSuggestions(q, session, country, language, ServedLanguages.LanguageOf(httpContext)), cancellationToken))
            .ToTypedHttpResult(list => TypedResults.Ok(new AddressSuggestionListResponse([.. list.Select(s => new AddressSuggestionResponse(s.Id, s.Text, s.MainText, s.SecondaryText))])));

    private static async Task<Results<Ok<AddressResponse>, ErrorsProblemHttpResult>> ValidateAsync(
        AddressValidationRequest request, IQueryHandler<ResolveAddress, AddressSnapshot> handler, HttpContext httpContext, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ResolveAddress(request.Reference ?? string.Empty, request.Session, request.Language, ServedLanguages.LanguageOf(httpContext)), cancellationToken))
            .ToTypedHttpResult(a => TypedResults.Ok(new AddressResponse(
                a.Reference, a.FormattedAddress, a.Street, a.StreetNumber, a.PostalCode, a.Locality, a.CountryCode, a.SubdivisionCode,
                a.Coordinates is { } point ? new AddressCoordinatesResponse(point.Latitude, point.Longitude) : null)));
}
