using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Frappe.Geo.IntegrationTests.Addresses;

/// <summary>
/// Google Places API (New) on a loopback socket, answering the recorded fixtures for one place id. Unlike
/// <see cref="FakePlaces"/>, the Api's HttpClient keeps its real handler, so the client span is emitted as in production.
/// </summary>
internal sealed class PlacesStub : IAsyncDisposable
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "places");

    private readonly WebApplication _app;

    private PlacesStub(WebApplication app, string placeId)
    {
        _app = app;
        PlaceId = placeId;
    }

    public string PlaceId { get; }

    /// <summary>The <c>Geo:Google:Places:BaseUrl</c> that reaches the stub.</summary>
    public Uri BaseUrl => new(new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()), "/v1/");

    public static async Task<PlacesStub> StartAsync(string placeId)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapPost("/v1/places:autocomplete", () => Fixture("autocomplete.json", placeId));
        app.MapGet("/v1/places/{id}", (string id) => id == placeId ? Fixture("details.json", placeId) : HttpResults.NotFound());
        await app.StartAsync();
        return new PlacesStub(app, placeId);
    }

    /// <summary>The fixture with its recorded place id replaced by <paramref name="placeId"/>.</summary>
    private static IResult Fixture(string name, string placeId) =>
        HttpResults.Text(File.ReadAllText(Path.Combine(Folder, name)).Replace(FakePlaces.KnownPlace, placeId, StringComparison.Ordinal), "application/json");

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
