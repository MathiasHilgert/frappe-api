using System.Text;
using System.Text.Json;
using Alba;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.TestUtilities;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using Wolverine.Runtime;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Frappe.Host.IntegrationTests;

public sealed class HttpSurfaceTests(FrappeDatabase database, FrappeNats nats)
{
    // The routes the host serves anonymously, declared so a new one shows up in review.
    private static readonly HashSet<string> _anonymousRoutes = new(StringComparer.Ordinal)
    {
        "/v1/geo/time-zones",
        "/v1/geo/currencies",
        "/v1/geo/countries",
        "/v1/geo/countries/{countryCode}",
        "/v1/geo/countries/{countryCode}/subdivisions",
        "/v1/geo/locales",
    };

    [Fact]
    public async Task Messages_write_dates_as_their_primitive()
    {
        await using IAlbaHost host = await StartAsync();
        IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();

        byte[] message = runtime.Options.DefaultSerializer.WriteMessage(
            new DateNoted(new LocalDate(2026, 4, 2))
        );

        Encoding.UTF8.GetString(message).ShouldBe("{\"on\":\"2026-04-02\"}");
    }

    // A module's value objects travel as their primitive through the module JSON hook, in responses and messages.
    [Fact]
    public async Task Responses_write_module_value_objects_as_their_primitive()
    {
        await using IAlbaHost host = await StartAsync();
        HttpJsonOptions options = host
            .Services.GetRequiredService<IOptions<HttpJsonOptions>>()
            .Value;

        string json = JsonSerializer.Serialize(
            new CountryNoted(CountryCode.Create("ar").Value, MinorUnit.Create(2).Value),
            options.SerializerOptions
        );

        json.ShouldBe("{\"country\":\"AR\",\"digits\":2}");
    }

    [Fact]
    public async Task Messages_write_module_value_objects_as_their_primitive()
    {
        await using IAlbaHost host = await StartAsync();
        IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();

        byte[] message = runtime.Options.DefaultSerializer.WriteMessage(
            new CountryNoted(CountryCode.Create("ar").Value, MinorUnit.Create(2).Value)
        );

        Encoding.UTF8.GetString(message).ShouldBe("{\"country\":\"AR\",\"digits\":2}");
    }

    [Fact]
    public async Task Every_host_route_follows_the_route_standard()
    {
        await using IAlbaHost host = await StartAsync();

        IReadOnlyList<string> violations = RouteStandard.Violations(
            host.Services.GetRequiredService<EndpointDataSource>(),
            _anonymousRoutes
        );

        violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_host_openapi_document_is_fully_described_and_matches_its_snapshot()
    {
        await using IAlbaHost host = await StartAsync();

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/openapi/v1.json");
            scenario.StatusCodeShouldBeOk();
        });

        string body = await result.ReadAsTextAsync();
        using var document = JsonDocument.Parse(body);
        OpenApiStandard.Violations(document.RootElement).ShouldBeEmpty();
        Snapshot.ShouldMatch(body, "openapi-host");
    }

    // Each module adds its own XML-doc descriptions, yet the document and its services stay registered once.
    [Fact]
    public async Task The_host_registers_one_openapi_document()
    {
        await using IAlbaHost host = await StartAsync();

        IEnumerable<IOpenApiDocumentProvider> documents =
            host.Services.GetKeyedServices<IOpenApiDocumentProvider>("v1");

        documents.ShouldHaveSingleItem();
    }

    private Task<IAlbaHost> StartAsync() =>
        AlbaHost.For<Program>(builder =>
            builder
                .UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
                .UseSetting("Nats:Url", nats.Url)
        );

    private sealed record DateNoted(LocalDate On);

    private sealed record CountryNoted(CountryCode Country, MinorUnit Digits);
}
