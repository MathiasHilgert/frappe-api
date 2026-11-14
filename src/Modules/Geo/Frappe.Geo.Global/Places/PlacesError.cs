using System.Net;
using System.Text.Json;

namespace Frappe.Geo.Global.Places;

/// <summary>
/// A Places API (New) error answer, read from its body: <c>google.rpc.Status</c> as JSON (https://google.aip.dev/193),
/// <c>{ "error": { "code", "message", "status", "details": [...] } }</c>, where a <c>google.rpc.ErrorInfo</c> detail
/// names the machine-readable <c>reason</c> (<c>API_KEY_INVALID</c>, <c>API_KEY_EXPIRED</c>, <c>RATE_LIMIT_EXCEEDED</c>
/// and the rest of https://github.com/googleapis/googleapis/blob/master/google/api/error_reason.proto) and a
/// <c>google.rpc.BadRequest</c> detail the fields that were wrong. The message is never logged: it may echo the place id.
/// </summary>
internal sealed record PlacesError(HttpStatusCode Code, string? Status, string? Reason, string? Message, IReadOnlyList<string> Fields)
{
    private const string ErrorInfoType = "type.googleapis.com/google.rpc.ErrorInfo";
    private const string BadRequestType = "type.googleapis.com/google.rpc.BadRequest";

    /// <summary>The request fields that carry the place id (<c>places/{place_id}</c> is the resource <c>name</c>).</summary>
    private static readonly string[] PlaceFields = ["place_id", "placeId", "name", "id"];

    /// <summary>
    /// Whether the error says the place itself is unknown: a 404 <c>NOT_FOUND</c>, or a 400 <c>INVALID_ARGUMENT</c> that
    /// blames the place id (a field violation on it, or a message naming it). Anything with an <c>ErrorInfo</c> reason
    /// (a rejected or expired key, a blocked API, an exhausted quota), any other argument, or a body that is not
    /// Google's error (a proxy, a wrong base URL) is about the call, not the place: the address is unavailable, not gone.
    /// </summary>
    public bool IsAboutThePlace => Reason is null && (Code, Status) switch
    {
        (HttpStatusCode.NotFound, "NOT_FOUND") => true,
        (HttpStatusCode.BadRequest, "INVALID_ARGUMENT") =>
            Fields.Any(f => PlaceFields.Contains(f, StringComparer.Ordinal))
            || (Message?.Contains("place id", StringComparison.OrdinalIgnoreCase) ?? false),
        _ => false,
    };

    /// <summary>What can be logged: the code name and the reason, never the message.</summary>
    public string Summary => $"{Status ?? "unknown"}/{Reason ?? "none"}";

    public static async Task<PlacesError> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                return Unreadable(response.StatusCode);
            }

            var details = error.TryGetProperty("details", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToArray() : [];
            return new PlacesError(
                response.StatusCode,
                StringOf(error, "status"),
                details.Where(d => StringOf(d, "@type") == ErrorInfoType).Select(d => StringOf(d, "reason")).FirstOrDefault(r => r is not null),
                StringOf(error, "message"),
                [.. details.Where(d => StringOf(d, "@type") == BadRequestType).SelectMany(FieldsOf)]);
        }
        catch (JsonException)
        {
            return Unreadable(response.StatusCode);
        }
    }

    private static PlacesError Unreadable(HttpStatusCode code) => new(code, null, null, null, []);

    private static IEnumerable<string> FieldsOf(JsonElement badRequest) =>
        badRequest.TryGetProperty("fieldViolations", out var violations) && violations.ValueKind == JsonValueKind.Array
            ? violations.EnumerateArray().Select(v => StringOf(v, "field")).OfType<string>()
            : [];

    private static string? StringOf(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
