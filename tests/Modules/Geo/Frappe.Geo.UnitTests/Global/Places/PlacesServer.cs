using System.Net;
using System.Text;
using Frappe.Observability;

namespace Frappe.Geo.UnitTests.Global.Places;

/// <summary>
/// Google Places API (New) as recorded fixtures (<c>Fixtures/places</c>): Autocomplete answers <c>autocomplete.json</c>,
/// Place Details answers the fixture named after the place id, or Google's 404. Never reaches the network.
/// </summary>
internal sealed class PlacesServer : HttpMessageHandler
{
    public const string KnownPlace = "ChIJ3S-JXmauEmsRUcIaWtf4MzE";
    public const string UnmappedPlace = "ChIJunmappedPlace0000000000";

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "places");

    public List<Recorded> Requests { get; } = [];

    /// <summary>When set, every request answers this status with an empty error body.</summary>
    public HttpStatusCode? Failure { get; set; }

    /// <summary>When set, every request answers this status and body (an error body as Google writes them, or anything else).</summary>
    public (HttpStatusCode Status, string Body)? Answer { get; set; }

    /// <summary>When set, Autocomplete answers this body instead of <c>autocomplete.json</c>.</summary>
    public string? Suggestions { get; set; }

    /// <summary>When set, every request throws it, as a broken connection or a resilience timeout would.</summary>
    public Exception? Throw { get; set; }

    /// <summary>A request as sent; <see cref="TelemetryUrl"/> is the URL traces show instead of <see cref="Uri"/>, when the adapter set one.</summary>
    public sealed record Recorded(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body, string? TelemetryUrl);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Recorded(request.Method, request.RequestUri!, headers, body, OutgoingRequestTelemetry.TelemetryUrlOf(request)));
        if (Throw is not null)
        {
            throw Throw;
        }

        if (Answer is { } answer)
        {
            return new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body, Encoding.UTF8, "application/json") };
        }

        if (Failure is { } status)
        {
            return new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }

        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post && path == "/v1/places:autocomplete")
        {
            return Suggestions is { } suggestions
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(suggestions, Encoding.UTF8, "application/json") }
                : Json(HttpStatusCode.OK, "autocomplete.json");
        }

        return path switch
        {
            "/v1/places/" + KnownPlace => Json(HttpStatusCode.OK, "details.json"),
            "/v1/places/" + UnmappedPlace => Json(HttpStatusCode.OK, "details-unmapped.json"),
            _ => Json(HttpStatusCode.NotFound, "not-found.json"),
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string fixture) =>
        new(status) { Content = new StringContent(File.ReadAllText(Path.Combine(Folder, fixture)), Encoding.UTF8, "application/json") };
}
