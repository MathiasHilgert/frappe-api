using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Contracts.Queries;
using Frappe.Geo.Global.Places;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Geo.IntegrationTests.Addresses;

/// <summary>
/// Addresses through the real Api over a synced territory, with Google Places answered by recorded fixtures (S11, S12):
/// opaque ids, sessions, normalization to ISO codes through Geo's aliases, no-store, and 503 without a key.
/// </summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class AddressEndpointsTests(GeoDatabase database, ValkeyCache valkey)
{
    private const string Session = "5b1c1a5e-3f5d-4e0b-9f53-4d1c2b9e8a10";
    private static readonly SemaphoreSlim Once = new(1, 1);
    private static readonly HashSet<string> Synced = [];

    private sealed record Answer(HttpStatusCode Status, JsonElement Body, string? CacheControl);

    private async Task<WebApplication> StartAsync(FakePlaces places, bool withKey = true)
    {
        await Once.WaitAsync();
        try
        {
            if (Synced.Add(database.Token))
            {
                await SourceSync.TerritoryAsync(database, new SourceServer());
            }
        }
        finally
        {
            Once.Release();
        }

        return await GeoApi.StartAsync(database, valkey, builder =>
        {
            // A placeholder, never a real key: the fake handler answers instead of Google.
            builder.Configuration[$"{GooglePlacesOptions.Section}:ApiKey"] = withKey ? "not-a-real-key" : null;
            builder.Services.AddHttpClient<GooglePlacesProvider>().ConfigurePrimaryHttpMessageHandler(() => places);
        });
    }

    private static async Task<Answer> GetAsync(WebApplication app, string path)
    {
        using var response = await app.GetTestClient().GetAsync(path);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new Answer(response.StatusCode, document.RootElement.Clone(), response.Headers.CacheControl?.ToString());
    }

    private static async Task<Answer> ValidateAsync(WebApplication app, object body)
    {
        using var response = await app.GetTestClient().PostAsJsonAsync("/v1/geo/address-validations", body);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new Answer(response.StatusCode, document.RootElement.Clone(), response.Headers.CacheControl?.ToString());
    }

    private static string FirstId(Answer suggestions) => suggestions.Body.GetProperty("suggestions")[0].GetProperty("id").GetString()!;

    [Test]
    public async Task Suggestions_answer_opaque_ids_and_are_never_cached()
    {
        await using var app = await StartAsync(new FakePlaces());

        var answer = await GetAsync(app, $"/v1/geo/address-suggestions?q=corrientes%201234&session={Session}&country=AR&language=es");

        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(answer.CacheControl).IsEqualTo("no-store");
        var first = answer.Body.GetProperty("suggestions")[0];
        await Assert.That(first.GetProperty("mainText").GetString()).IsEqualTo("Avenida Corrientes 1234");
        await Assert.That(first.GetProperty("id").GetString()).DoesNotContain(FakePlaces.KnownPlace);
        await Assert.That(answer.Body.GetProperty("suggestions").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task A_suggestion_resolves_to_the_snapshot_with_the_province_mapped_to_ISO_through_its_alias()
    {
        await using var app = await StartAsync(new FakePlaces());
        var id = FirstId(await GetAsync(app, $"/v1/geo/address-suggestions?q=corrientes&session={Session}"));

        var answer = await ValidateAsync(app, new { reference = id, session = Session, language = "es" });

        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(answer.CacheControl).IsEqualTo("no-store");
        var body = answer.Body;
        await Assert.That(body.GetProperty("reference").GetString()).IsEqualTo(id);
        await Assert.That(body.GetProperty("street").GetString()).IsEqualTo("Avenida Corrientes");
        await Assert.That(body.GetProperty("streetNumber").GetString()).IsEqualTo("1234");
        await Assert.That(body.GetProperty("postalCode").GetString()).IsEqualTo("C1043");
        await Assert.That(body.GetProperty("countryCode").GetString()).IsEqualTo("AR");
        await Assert.That(body.GetProperty("subdivisionCode").GetString()).IsEqualTo("AR-C");
        await Assert.That(body.GetProperty("coordinates").GetProperty("latitude").GetDouble()).IsEqualTo(-34.6037851);
    }

    [Test]
    public async Task A_province_Geo_does_not_know_answers_a_null_subdivision_code()
    {
        await using var app = await StartAsync(new FakePlaces());
        var reference = new AddressReference(GooglePlacesProvider.ProviderName, FakePlaces.UnmappedPlace).Encode();

        var answer = await ValidateAsync(app, new { reference, session = Session });

        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(answer.Body.GetProperty("countryCode").GetString()).IsEqualTo("AR");
        await Assert.That(answer.Body.GetProperty("subdivisionCode").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task A_forged_id_or_a_missing_session_is_400_and_never_reaches_Google()
    {
        var places = new FakePlaces();
        await using var app = await StartAsync(places);

        var forged = await ValidateAsync(app, new { reference = FakePlaces.KnownPlace, session = Session });
        var missing = await ValidateAsync(app, new { session = Session });
        var noSession = await GetAsync(app, "/v1/geo/address-suggestions?q=corrientes");

        await Assert.That(forged.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(forged.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.address_reference.invalid");
        await Assert.That(missing.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missing.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.parameter.required");
        await Assert.That(missing.Body.GetProperty("errors")[0].GetProperty("parameters").GetProperty("parameter").GetString()).IsEqualTo("reference");
        await Assert.That(noSession.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(noSession.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.parameter.required");
        await Assert.That(noSession.Body.GetProperty("errors")[0].GetProperty("parameters").GetProperty("parameter").GetString()).IsEqualTo("session");
        await Assert.That(places.Requests).IsEqualTo(0);
    }

    [Test]
    public async Task A_place_Google_no_longer_knows_is_404()
    {
        await using var app = await StartAsync(new FakePlaces());
        var reference = new AddressReference(GooglePlacesProvider.ProviderName, "ChIJgone").Encode();

        var answer = await ValidateAsync(app, new { reference, session = Session });

        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(answer.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.address.not_found");
    }

    [Test]
    public async Task A_key_Google_rejects_makes_a_stored_address_unavailable_never_gone()
    {
        // Synthetic, in the google.rpc.Status shape Google documents (https://google.aip.dev/193).
        var places = new FakePlaces
        {
            Answer = (HttpStatusCode.BadRequest, """
                { "error": { "code": 400, "message": "API key not valid. Please pass a valid API key.", "status": "INVALID_ARGUMENT",
                  "details": [ { "@type": "type.googleapis.com/google.rpc.ErrorInfo", "reason": "API_KEY_INVALID", "domain": "googleapis.com" } ] } }
                """),
        };
        await using var app = await StartAsync(places);
        var reference = new AddressReference(GooglePlacesProvider.ProviderName, FakePlaces.KnownPlace).Encode();

        using var response = await app.GetTestClient().PostAsJsonAsync("/v1/geo/address-validations", new { reference, session = Session });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await using var scope = app.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IGeoQueries>().ResolveAddressAsync(reference, null, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(body.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.addresses.unavailable");
        await Assert.That(response.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(stored.Status).IsEqualTo(AddressStatus.Unavailable);
    }

    [Test]
    public async Task Without_an_api_key_the_host_starts_and_addresses_answer_503_not_configured()
    {
        var places = new FakePlaces();
        await using var app = await StartAsync(places, withKey: false);
        using var response = await app.GetTestClient().GetAsync($"/v1/geo/address-suggestions?q=corrientes&session={Session}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(body.RootElement.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:unavailable");
        await Assert.That(body.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.addresses.not_configured");
        await Assert.That(response.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(places.Requests).IsEqualTo(0);
    }

    [Test]
    public async Task A_malformed_request_is_400_before_the_missing_provider_is_503()
    {
        var places = new FakePlaces();
        await using var app = await StartAsync(places, withKey: false);

        var unknownCountry = await GetAsync(app, $"/v1/geo/address-suggestions?q=corrientes&session={Session}&country=XX");
        var noSession = await GetAsync(app, "/v1/geo/address-suggestions?q=corrientes");

        await Assert.That(unknownCountry.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(unknownCountry.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.country_code.invalid_format");
        await Assert.That(noSession.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(noSession.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.parameter.required");
    }

    [Test]
    public async Task An_unknown_country_filter_is_400_with_a_configured_provider_too_and_never_reaches_Google()
    {
        var places = new FakePlaces();
        await using var app = await StartAsync(places);

        var answer = await GetAsync(app, $"/v1/geo/address-suggestions?q=corrientes&session={Session}&country=XX");

        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(answer.Body.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("geo.country_code.invalid_format");
        await Assert.That(places.Requests).IsEqualTo(0);
    }

    [Test]
    public async Task Other_modules_re_validate_a_stored_reference_through_IGeoQueries()
    {
        await using var app = await StartAsync(new FakePlaces());
        var reference = new AddressReference(GooglePlacesProvider.ProviderName, FakePlaces.KnownPlace).Encode();
        await using var scope = app.Services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IGeoQueries>();

        var resolved = await queries.ResolveAddressAsync(reference, "es", CancellationToken.None);
        var gone = await queries.ResolveAddressAsync(new AddressReference(GooglePlacesProvider.ProviderName, "ChIJgone").Encode(), null, CancellationToken.None);
        var invalid = await queries.ResolveAddressAsync("not a reference", null, CancellationToken.None);
        var badLanguage = await queries.ResolveAddressAsync(reference, "not a language", CancellationToken.None);

        await Assert.That(resolved.Status).IsEqualTo(AddressStatus.Resolved);
        await Assert.That(resolved.Address!.SubdivisionCode).IsEqualTo("AR-C");
        await Assert.That(resolved.Address.Coordinates).IsEqualTo(new GeoCoordinates(-34.6037851, -58.3815655));
        await Assert.That(gone.Status).IsEqualTo(AddressStatus.NotFound);
        await Assert.That(invalid.Status).IsEqualTo(AddressStatus.InvalidReference);
        await Assert.That(badLanguage.Status).IsEqualTo(AddressStatus.InvalidLanguage);
    }
}
