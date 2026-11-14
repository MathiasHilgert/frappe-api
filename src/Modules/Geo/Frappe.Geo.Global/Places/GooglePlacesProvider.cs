using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Application.Territory;
using Frappe.Localization;
using Frappe.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Frappe.Geo.Global.Places;

/// <summary>
/// Addresses from Google Places API (New): Autocomplete (<c>POST places:autocomplete</c>) while the user types and Place
/// Details (<c>GET places/{id}</c>) for the chosen place, grouped by the session token so Google bills them as one
/// search, asking only for Essentials fields. Docs: https://developers.google.com/maps/documentation/places/web-service/place-autocomplete,
/// .../place-details and .../place-session-tokens. It never logs what was typed, the place or its address, and traces
/// show Place Details as <c>places/REDACTED</c>, since its path carries the place id (S15).
/// </summary>
internal sealed partial class GooglePlacesProvider(HttpClient http, IOptions<GooglePlacesOptions> options, ILogger<GooglePlacesProvider> logger)
    : IAddressProvider
{
    public const string ProviderName = "google";

    /// <summary>Autocomplete bills by request, not by field; the mask only keeps the answer small.</summary>
    public const string AutocompleteFields = "suggestions.placePrediction.placeId,suggestions.placePrediction.text.text,suggestions.placePrediction.structuredFormat";

    /// <summary>Essentials fields only (SKU Place Details Essentials): a Pro or Enterprise field would bill the whole call at its tier.</summary>
    public const string DetailsFields = "id,formattedAddress,addressComponents,location";

    private const string ApiKeyHeader = "X-Goog-Api-Key";
    private const string FieldMaskHeader = "X-Goog-FieldMask";

    private readonly GooglePlacesOptions _options = options.Value;

    public string Name => ProviderName;

    /// <summary>
    /// Google publishes no session token lifetime: a session "begins with a call to Autocomplete (New), and concludes with
    /// a call to Place Details (New) or Address Validation", or is abandoned
    /// (https://developers.google.com/maps/documentation/places/web-service/place-session-tokens, checked 2026-11-14).
    /// 15 minutes is a conservative wait: a slow user's resolution still closes the session; a later one can only
    /// count a session as abandoned, never as completed twice.
    /// </summary>
    public TimeSpan SessionLifetime => TimeSpan.FromMinutes(15);

    public double EstimatedCostUsd(AddressOperation operation) =>
        operation == AddressOperation.Suggest ? GooglePlacesPricing.AutocompleteRequest : GooglePlacesPricing.PlaceDetailsEssentials;

    public async Task<IReadOnlyList<ProviderSuggestion>> SuggestAsync(AddressSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var body = new JsonObject
        {
            ["input"] = search.Input,
            ["sessionToken"] = search.Session.Value,
            ["languageCode"] = search.Language.Value,
        };
        if (search.Country is { } country)
        {
            body["includedRegionCodes"] = new JsonArray(RegionCodeOf(country.Value));
        }

        using var request = Request(HttpMethod.Post, new Uri(_options.BaseUrl.AbsoluteUri + "places:autocomplete"), AutocompleteFields);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var document = await SendAsync(request, "autocomplete", notFoundIsEmpty: false, cancellationToken);
        if (document is null || !document.RootElement.TryGetProperty("suggestions", out var suggestions))
        {
            return [];
        }

        return [.. suggestions.EnumerateArray()
            .Select(s => s.TryGetProperty("placePrediction", out var prediction) ? prediction : default)
            .Where(p => StringOf(p, "placeId") is { Length: > 0 })
            .Select(ToSuggestion)];
    }

    public async Task<ProviderAddress?> ResolveAsync(AddressReference reference, AddressSession session, LanguageTag language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(language);
        if (reference.Provider != ProviderName)
        {
            return null;
        }

        var uri = new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"{_options.BaseUrl.AbsoluteUri}places/{Uri.EscapeDataString(reference.ProviderId)}?sessionToken={Uri.EscapeDataString(session.Value)}&languageCode={Uri.EscapeDataString(language.Value)}"));
        using var request = Request(HttpMethod.Get, uri, DetailsFields)
            .ShowInTelemetryAs(_options.BaseUrl.AbsoluteUri + "places/" + OutgoingRequestTelemetry.Redacted);
        using var document = await SendAsync(request, "details", notFoundIsEmpty: true, cancellationToken);
        return document is null ? null : ToAddress(document.RootElement, reference.ProviderId);
    }

    /// <summary>Google takes ccTLD region codes, which equal ISO 3166-1 alpha-2 but for the United Kingdom (<c>uk</c>, not <c>gb</c>).</summary>
    private static string RegionCodeOf(string iso) => iso == "GB" ? "uk" : iso.ToLowerInvariant();

    private HttpRequestMessage Request(HttpMethod method, Uri uri, string fields)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new AddressProviderNotConfiguredException($"Google Places has no API key ({GooglePlacesOptions.Section}:ApiKey).");
        }

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(ApiKeyHeader, _options.ApiKey);
        request.Headers.Add(FieldMaskHeader, fields);
        return request;
    }

    /// <summary>
    /// The answer's JSON; <see langword="null"/>, when <paramref name="notFoundIsEmpty"/>, for an error Google says is
    /// about the place itself (<see cref="PlacesError.IsAboutThePlace"/>). Any other error (a rejected or expired key, an
    /// exhausted quota, a Google failure, an answer that is not Google's), a timeout or an unreadable body is unavailable:
    /// a stored address must never look gone because the call failed. The caller's cancellation propagates.
    /// </summary>
    private async Task<JsonDocument?> SendAsync(HttpRequestMessage request, string operation, bool notFoundIsEmpty, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await PlacesError.ReadAsync(response, cancellationToken);
                if (notFoundIsEmpty && error.IsAboutThePlace)
                {
                    return null;
                }

                LogRejected(logger, operation, (int)response.StatusCode, error.Summary);
                throw new AddressProviderUnavailableException($"Google Places {operation} answered {(int)response.StatusCode} ({error.Summary}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (IsProviderFailure(exception) && !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, operation, exception.GetType().Name);
            throw new AddressProviderUnavailableException($"Google Places {operation} did not answer ({exception.GetType().Name}).");
        }
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception is HttpRequestException or IOException or ExecutionRejectedException or OperationCanceledException or JsonException;

    private static ProviderSuggestion ToSuggestion(JsonElement prediction)
    {
        var format = prediction.TryGetProperty("structuredFormat", out var structured) ? structured : default;
        var text = TextOf(prediction, "text") ?? string.Empty;
        return new ProviderSuggestion(
            StringOf(prediction, "placeId")!,
            text,
            format.ValueKind == JsonValueKind.Object ? TextOf(format, "mainText") ?? text : text,
            format.ValueKind == JsonValueKind.Object ? TextOf(format, "secondaryText") : null);
    }

    /// <summary><c>{ "text": "..." }</c>, the shape of every formatted text in Places (New).</summary>
    private static string? TextOf(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.TryGetProperty("text", out var text) ? text.GetString() : null;

    private static ProviderAddress ToAddress(JsonElement place, string requestedId)
    {
        var components = place.TryGetProperty("addressComponents", out var list) ? list.EnumerateArray().ToArray() : [];
        return new ProviderAddress(
            StringOf(place, "id") ?? requestedId,
            StringOf(place, "formattedAddress") ?? string.Empty,
            LongText(components, "route"),
            LongText(components, "street_number"),
            LongText(components, "postal_code"),
            LongText(components, "locality"),
            Component(components, "country") is { } country ? StringOf(country, "shortText") : null,
            SubdivisionNames(components),
            CoordinatesOf(place));
    }

    private static string? StringOf(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? LongText(JsonElement[] components, string type) =>
        Component(components, type) is { } component ? StringOf(component, "longText") : null;

    /// <summary>The first-level subdivision's long name, then its short one (Places gives names, never ISO 3166-2 codes).</summary>
    private static string[] SubdivisionNames(JsonElement[] components)
    {
        if (Component(components, "administrative_area_level_1") is not { } subdivision)
        {
            return [];
        }

        string?[] names = [StringOf(subdivision, "longText"), StringOf(subdivision, "shortText")];
        return [.. names.OfType<string>().Where(n => n.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    private static Coordinates? CoordinatesOf(JsonElement place) =>
        place.TryGetProperty("location", out var location)
        && location.TryGetProperty("latitude", out var latitude) && location.TryGetProperty("longitude", out var longitude)
            ? new Coordinates(latitude.GetDouble(), longitude.GetDouble())
            : null;

    private static JsonElement? Component(JsonElement[] components, string type) =>
        components.FirstOrDefault(c => c.TryGetProperty("types", out var types) && types.EnumerateArray().Any(t => t.GetString() == type)) is { ValueKind: JsonValueKind.Object } found
            ? found
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Places {Operation} answered {Status} ({Error}): addresses are unavailable")]
    private static partial void LogRejected(ILogger logger, string operation, int status, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google Places {Operation} did not answer ({Failure}): addresses are unavailable")]
    private static partial void LogFailed(ILogger logger, string operation, string failure);
}
