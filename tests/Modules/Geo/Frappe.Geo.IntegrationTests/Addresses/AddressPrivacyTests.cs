using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Contracts.Queries;
using Frappe.Geo.Global.Places;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;
using Frappe.Geo.IntegrationTests.Sync;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Geo.IntegrationTests.Addresses;

/// <summary>
/// S15: the place a user chose never reaches telemetry. The real Api calls a Places stub over a real socket, so the
/// HttpClient span exists; every span and every log (with its scopes) of a search and a validation is read back.
/// </summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class AddressPrivacyTests(GeoDatabase database, ValkeyCache valkey)
{
    private const string Session = "0f9e4c1a-6b2d-4c8e-9a71-3d5e2f4b6c80";

    [Test]
    public async Task Neither_spans_nor_logs_carry_the_place_or_its_reference()
    {
        // Unique to this test: activity listeners are process-wide, and other tests resolve the recorded place.
        var placeId = "ChIJprivacy" + Guid.NewGuid().ToString("N");
        await using var places = await PlacesStub.StartAsync(placeId);
        await SourceSync.TerritoryAsync(database, new SourceServer());
        ConcurrentQueue<Activity> spans = new();
        using var listener = Listen(spans);
        var logs = new ScopedLogs();
        await using var app = await GeoApi.StartAsync(database, valkey, builder =>
        {
            builder.Configuration[$"{GooglePlacesOptions.Section}:ApiKey"] = "not-a-real-key";
            builder.Configuration[$"{GooglePlacesOptions.Section}:BaseUrl"] = places.BaseUrl.AbsoluteUri;
            builder.Logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs);
        });
        var client = app.GetTestClient();

        using var suggestions = JsonDocument.Parse(await client.GetStringAsync($"/v1/geo/address-suggestions?q=corrientes&session={Session}"));
        var reference = suggestions.RootElement.GetProperty("suggestions")[0].GetProperty("id").GetString()!;
        using var validated = await client.PostAsJsonAsync("/v1/geo/address-validations", new { reference, session = Session, language = "es" });
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var again = await scope.ServiceProvider.GetRequiredService<IGeoQueries>().ResolveAddressAsync(reference, "es", CancellationToken.None);
            await Assert.That(again.Status).IsEqualTo(AddressStatus.Resolved);
        }

        await Assert.That(validated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(reference).IsEqualTo(new AddressReference(GooglePlacesProvider.ProviderName, placeId).Encode());
        var details = new Uri(places.BaseUrl, "places/REDACTED").AbsoluteUri;
        await Assert.That(spans.Count(s => s.Kind == ActivityKind.Client && Equals(s.GetTagItem("url.full"), details))).IsGreaterThanOrEqualTo(2);
        await Assert.That(spans.Any(s => s.Kind == ActivityKind.Server && Equals(s.GetTagItem("url.path"), "/v1/geo/address-validations"))).IsTrue();
        await Assert.That(logs.Entries.Any(e => e.Scopes.Any(s => s.Key == "RequestPath" && Equals(s.Value, "/v1/geo/address-validations")))).IsTrue();

        // The stub's own server spans are Google's side of the call, not Frappé's telemetry.
        var ours = spans.Where(s => !(s.Kind == ActivityKind.Server && (s.GetTagItem("url.path") as string ?? string.Empty).StartsWith("/v1/places", StringComparison.Ordinal)));
        var telemetry = string.Join("\n", ours.Select(s => s.DisplayName + " " + string.Join(" ", s.TagObjects.Select(t => $"{t.Key}={t.Value}")))) + "\n" + logs.Text();
        await Assert.That(telemetry).DoesNotContain(placeId).And.DoesNotContain(reference);
    }

    /// <summary>Every span of the process, once stopped, as an exporter would receive it.</summary>
    private static ActivityListener Listen(ConcurrentQueue<Activity> spans)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
