using System.Net;
using System.Text.Json;
using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Global.Places;
using Frappe.Localization;
using Frappe.Testing;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace Frappe.Geo.UnitTests.Global.Places;

/// <summary>
/// The Google adapter against recorded Places API (New) answers: what it sends (endpoint, key, field mask, session) and
/// how it reads suggestions and address components. Docs: developers.google.com/maps/documentation/places/web-service.
/// </summary>
public sealed class GooglePlacesProviderTests
{
    private const string Key = "not-a-real-key";

    private static readonly AddressSession Session = AddressSession.Create("5b1c1a5e-3f5d-4e0b-9f53-4d1c2b9e8a10").Value;
    private static readonly LanguageTag Spanish = LanguageTag.Create("es").Value;

    private static (GooglePlacesProvider Provider, PlacesServer Server, CapturedLogs Logs) Create(string? key = Key)
    {
        var server = new PlacesServer();
        var logs = new CapturedLogs();
        var provider = new GooglePlacesProvider(
            new HttpClient(server),
            Options.Create(new GooglePlacesOptions { ApiKey = key }),
            logs.For<GooglePlacesProvider>());
        return (provider, server, logs);
    }

    private static AddressSearch Search(string input = "corrientes 1234") =>
        new(input, Session, CountryCode.Create("AR").Value, Spanish);

    [Test]
    public async Task Suggestions_post_the_input_with_the_session_country_and_language_and_ask_only_for_the_fields_used()
    {
        var (provider, server, _) = Create();

        await provider.SuggestAsync(Search(), CancellationToken.None);

        var request = server.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.Uri.AbsoluteUri).IsEqualTo("https://places.googleapis.com/v1/places:autocomplete");
        await Assert.That(request.Headers["X-Goog-Api-Key"]).IsEqualTo(Key);
        await Assert.That(request.Headers["X-Goog-FieldMask"])
            .IsEqualTo("suggestions.placePrediction.placeId,suggestions.placePrediction.text.text,suggestions.placePrediction.structuredFormat");
        using var body = JsonDocument.Parse(request.Body!);
        await Assert.That(body.RootElement.GetProperty("input").GetString()).IsEqualTo("corrientes 1234");
        await Assert.That(body.RootElement.GetProperty("sessionToken").GetString()).IsEqualTo(Session.Value);
        await Assert.That(body.RootElement.GetProperty("languageCode").GetString()).IsEqualTo("es");
        await Assert.That(body.RootElement.GetProperty("includedRegionCodes")[0].GetString()).IsEqualTo("ar");
    }

    [Test]
    public async Task Suggestions_keep_place_predictions_only_with_their_main_and_secondary_text()
    {
        var (provider, _, _) = Create();

        var suggestions = await provider.SuggestAsync(Search(), CancellationToken.None);

        await Assert.That(suggestions.Count).IsEqualTo(2);
        await Assert.That(suggestions[0]).IsEqualTo(new ProviderSuggestion(
            PlacesServer.KnownPlace, "Avenida Corrientes 1234, Buenos Aires, Argentina", "Avenida Corrientes 1234", "Buenos Aires, Argentina"));
        await Assert.That(suggestions[1].SecondaryText).IsNull();
    }

    [Test]
    public async Task Resolving_gets_the_place_with_the_session_and_an_essentials_only_field_mask()
    {
        var (provider, server, _) = Create();

        await provider.ResolveAsync(new AddressReference(GooglePlacesProvider.ProviderName, PlacesServer.KnownPlace), Session, Spanish, CancellationToken.None);

        var request = server.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(request.Uri.AbsoluteUri)
            .IsEqualTo($"https://places.googleapis.com/v1/places/{PlacesServer.KnownPlace}?sessionToken={Session.Value}&languageCode=es");
        await Assert.That(request.Headers["X-Goog-Api-Key"]).IsEqualTo(Key);
        await Assert.That(request.Headers["X-Goog-FieldMask"]).IsEqualTo("id,formattedAddress,addressComponents,location");
    }

    [Test]
    public async Task Resolving_maps_the_address_components_to_the_canonical_parts()
    {
        var (provider, _, _) = Create();

        var address = await provider.ResolveAsync(new AddressReference(GooglePlacesProvider.ProviderName, PlacesServer.KnownPlace), Session, Spanish, CancellationToken.None);

        await Assert.That(address).IsNotNull();
        await Assert.That(address!.ProviderId).IsEqualTo(PlacesServer.KnownPlace);
        await Assert.That(address.Street).IsEqualTo("Avenida Corrientes");
        await Assert.That(address.StreetNumber).IsEqualTo("1234");
        await Assert.That(address.PostalCode).IsEqualTo("C1043");
        await Assert.That(address.Locality).IsEqualTo("Buenos Aires");
        await Assert.That(address.CountryCode).IsEqualTo("AR");
        await Assert.That(address.SubdivisionNames).IsEquivalentTo(["Capital Federal", "CABA"]);
        await Assert.That(address.Coordinates).IsEqualTo(new Frappe.Geo.Application.Territory.Coordinates(-34.6037851, -58.3815655));
        await Assert.That(address.FormattedAddress).IsEqualTo("Av. Corrientes 1234, C1043 Cdad. Autónoma de Buenos Aires, Argentina");
    }

    [Test]
    public async Task An_unknown_place_or_another_providers_reference_resolves_to_nothing()
    {
        var (provider, server, _) = Create();

        var unknown = await provider.ResolveAsync(new AddressReference(GooglePlacesProvider.ProviderName, "ChIJdoesNotExist"), Session, Spanish, CancellationToken.None);
        var foreign = await provider.ResolveAsync(new AddressReference("other", PlacesServer.KnownPlace), Session, Spanish, CancellationToken.None);

        await Assert.That(unknown).IsNull();
        await Assert.That(foreign).IsNull();
        await Assert.That(server.Requests.Count).IsEqualTo(1);
    }

    // Error bodies follow google.rpc.Status as JSON (https://google.aip.dev/193): error.code (HTTP), error.status
    // (google.rpc.Code name), error.message and error.details, where a google.rpc.ErrorInfo names the reason
    // (https://github.com/googleapis/googleapis/blob/master/google/api/error_reason.proto lists API_KEY_INVALID and the
    // other key and quota reasons). The 400 bodies below are synthetic in that shape, not recorded from Google.
    private const string KeyInvalid = """
        { "error": { "code": 400, "message": "API key not valid. Please pass a valid API key.", "status": "INVALID_ARGUMENT",
          "details": [ { "@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "API_KEY_INVALID", "domain": "googleapis.com",
            "metadata": { "service": "places.googleapis.com" } } ] } }
        """;

    private const string KeyExpired = """
        { "error": { "code": 400, "message": "API key expired. Please renew the API key.", "status": "INVALID_ARGUMENT",
          "details": [ { "@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "API_KEY_EXPIRED", "domain": "googleapis.com" } ] } }
        """;

    private const string LanguageInvalid = """
        { "error": { "code": 400, "message": "Invalid language code.", "status": "INVALID_ARGUMENT" } }
        """;

    private const string PlaceIdInvalid = """
        { "error": { "code": 400, "message": "Not a valid Place ID: ChIJdoesNotExist", "status": "INVALID_ARGUMENT" } }
        """;

    private const string PlaceIdViolation = """
        { "error": { "code": 400, "message": "Request contains an invalid argument.", "status": "INVALID_ARGUMENT",
          "details": [ { "@type": "type.googleapis.com/google.rpc.BadRequest", "fieldViolations": [ { "field": "place_id" } ] } ] } }
        """;

    private const string PlaceNotFound = """
        { "error": { "code": 404, "message": "It looks like the Place ID is no longer valid.", "status": "NOT_FOUND" } }
        """;

    private static readonly AddressReference Gone = new(GooglePlacesProvider.ProviderName, "ChIJdoesNotExist");

    [Test]
    [Arguments(HttpStatusCode.BadRequest, KeyInvalid)]
    [Arguments(HttpStatusCode.BadRequest, KeyExpired)]
    [Arguments(HttpStatusCode.BadRequest, LanguageInvalid)]
    [Arguments(HttpStatusCode.BadRequest, "{}")]
    [Arguments(HttpStatusCode.BadRequest, "<html>Bad Request</html>")]
    [Arguments(HttpStatusCode.NotFound, "<html>Not Found</html>")]
    [Arguments(HttpStatusCode.NotFound, "{}")]
    public async Task A_details_error_that_is_not_about_the_place_is_unavailable_never_a_missing_address(HttpStatusCode status, string body)
    {
        var (provider, server, logs) = Create();
        server.Answer = (status, body);

        await Assert.ThrowsAsync<AddressProviderUnavailableException>(async () => await provider.ResolveAsync(Gone, Session, Spanish, CancellationToken.None));
        await Assert.That(string.Join("\n", logs.Entries.Select(e => e.Message))).DoesNotContain(Gone.ProviderId);
    }

    [Test]
    [Arguments(HttpStatusCode.NotFound, PlaceNotFound)]
    [Arguments(HttpStatusCode.BadRequest, PlaceIdInvalid)]
    [Arguments(HttpStatusCode.BadRequest, PlaceIdViolation)]
    public async Task A_details_error_about_the_place_itself_resolves_to_nothing(HttpStatusCode status, string body)
    {
        var (provider, server, _) = Create();
        server.Answer = (status, body);

        var address = await provider.ResolveAsync(Gone, Session, Spanish, CancellationToken.None);

        await Assert.That(address).IsNull();
    }

    [Test]
    public async Task A_prediction_without_a_place_id_is_skipped()
    {
        var (provider, server, _) = Create();
        server.Suggestions = """
            { "suggestions": [
              { "placePrediction": { "text": { "text": "No id" } } },
              { "placePrediction": { "placeId": "ChIJwithId", "text": { "text": "With id" } } } ] }
            """;

        var suggestions = await provider.SuggestAsync(Search(), CancellationToken.None);

        await Assert.That(suggestions.Select(s => s.ProviderId)).IsEquivalentTo(["ChIJwithId"]);
    }

    [Test]
    public async Task Details_requests_show_a_redacted_url_to_telemetry()
    {
        var (provider, server, _) = Create();

        await provider.ResolveAsync(new AddressReference(GooglePlacesProvider.ProviderName, PlacesServer.KnownPlace), Session, Spanish, CancellationToken.None);

        await Assert.That(server.Requests.Single().TelemetryUrl).IsEqualTo("https://places.googleapis.com/v1/places/REDACTED");
    }

    [Test]
    public async Task Without_an_api_key_it_is_unavailable_and_never_calls_Google()
    {
        var (provider, server, _) = Create(key: null);

        await Assert.ThrowsAsync<AddressProviderUnavailableException>(async () => await provider.SuggestAsync(Search(), CancellationToken.None));
        await Assert.ThrowsAsync<AddressProviderUnavailableException>(async () =>
            await provider.ResolveAsync(new AddressReference(GooglePlacesProvider.ProviderName, PlacesServer.KnownPlace), Session, Spanish, CancellationToken.None));
        await Assert.That(server.Requests).IsEmpty();
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.TooManyRequests)]
    [Arguments(HttpStatusCode.InternalServerError)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    public async Task A_rejected_key_an_exhausted_quota_or_a_Google_failure_is_unavailable(HttpStatusCode status)
    {
        var (provider, server, _) = Create();
        server.Failure = status;

        await Assert.ThrowsAsync<AddressProviderUnavailableException>(async () => await provider.SuggestAsync(Search(), CancellationToken.None));
        await Assert.ThrowsAsync<AddressProviderUnavailableException>(async () =>
            await provider.ResolveAsync(new AddressReference(GooglePlacesProvider.ProviderName, PlacesServer.KnownPlace), Session, Spanish, CancellationToken.None));
    }

    [Test]
    public async Task A_timeout_or_a_broken_connection_is_unavailable_but_the_callers_cancellation_propagates()
    {
        var (provider, server, _) = Create();

        server.Throw = new TimeoutRejectedException();
        await Assert.ThrowsAsync<AddressProviderUnavailableException>(async () => await provider.SuggestAsync(Search(), CancellationToken.None));
        server.Throw = new HttpRequestException("connection refused");
        await Assert.ThrowsAsync<AddressProviderUnavailableException>(async () => await provider.SuggestAsync(Search(), CancellationToken.None));

        server.Throw = null;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await provider.SuggestAsync(Search(), cancelled.Token));
    }

    [Test]
    public async Task Logs_never_carry_what_the_user_typed_the_place_or_the_address()
    {
        var (provider, server, logs) = Create();

        await provider.SuggestAsync(Search("calle secreta 742"), CancellationToken.None);
        await provider.ResolveAsync(new AddressReference(GooglePlacesProvider.ProviderName, PlacesServer.KnownPlace), Session, Spanish, CancellationToken.None);
        server.Failure = HttpStatusCode.InternalServerError;
        await Assert.ThrowsAsync<AddressProviderUnavailableException>(async () => await provider.SuggestAsync(Search("calle secreta 742"), CancellationToken.None));

        var text = string.Join("\n", logs.Entries.Select(e => e.Message + e.Exception));
        await Assert.That(text).DoesNotContain("secreta").And.DoesNotContain(PlacesServer.KnownPlace).And.DoesNotContain("Corrientes").And.DoesNotContain(Key);
    }
}
