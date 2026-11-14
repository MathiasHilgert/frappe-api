using System.Net;
using System.Text;

namespace Frappe.Geo.IntegrationTests.Addresses;

/// <summary>Google Places API (New) as the recorded fixtures in <c>Fixtures/places</c>; never reaches the network.</summary>
internal sealed class FakePlaces : HttpMessageHandler
{
    public const string KnownPlace = "ChIJ3S-JXmauEmsRUcIaWtf4MzE";
    public const string UnmappedPlace = "ChIJunmappedPlace0000000000";

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "places");

    public int Requests { get; private set; }

    /// <summary>When set, every request answers this status and body, as Google answers a rejected key or a failure.</summary>
    public (HttpStatusCode Status, string Body)? Answer { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (Answer is { } answer)
        {
            return Task.FromResult(new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body, Encoding.UTF8, "application/json") });
        }

        var path = request.RequestUri!.AbsolutePath;
        var (status, fixture) = path switch
        {
            "/v1/places:autocomplete" => (HttpStatusCode.OK, "autocomplete.json"),
            "/v1/places/" + KnownPlace => (HttpStatusCode.OK, "details.json"),
            "/v1/places/" + UnmappedPlace => (HttpStatusCode.OK, "details-unmapped.json"),
            _ => (HttpStatusCode.NotFound, "not-found.json"),
        };
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(File.ReadAllText(Path.Combine(Folder, fixture)), Encoding.UTF8, "application/json"),
        });
    }
}
