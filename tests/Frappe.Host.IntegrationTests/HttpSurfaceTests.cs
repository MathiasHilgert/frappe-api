using System.Text;
using System.Text.Json;
using Alba;
using Frappe.Kernel.Geography;
using Frappe.TestUtilities;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wolverine.Runtime;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Frappe.Host.IntegrationTests;

public sealed class HttpSurfaceTests(FrappeDatabase database, FrappeNats nats)
{
    // The routes the host serves anonymously, declared so a new one shows up in review.
    private static readonly HashSet<string> _anonymousRoutes = new(StringComparer.Ordinal);

    [Fact]
    public async Task Responses_write_kernel_value_objects_as_their_primitive()
    {
        await using IAlbaHost host = await StartAsync();
        HttpJsonOptions options = host
            .Services.GetRequiredService<IOptions<HttpJsonOptions>>()
            .Value;

        string json = JsonSerializer.Serialize(
            CountryCode.Create("ar").Value,
            options.SerializerOptions
        );

        json.ShouldBe("\"AR\"");
    }

    [Fact]
    public async Task Messages_write_kernel_value_objects_as_their_primitive()
    {
        await using IAlbaHost host = await StartAsync();
        IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();

        byte[] message = runtime.Options.DefaultSerializer.WriteMessage(
            new CountryNoted(CountryCode.Create("ar").Value)
        );

        Encoding.UTF8.GetString(message).ShouldBe("{\"country\":\"AR\"}");
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
    public async Task The_host_openapi_document_is_fully_described()
    {
        await using IAlbaHost host = await StartAsync();

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/openapi/v1.json");
            scenario.StatusCodeShouldBeOk();
        });

        using var document = JsonDocument.Parse(await result.ReadAsTextAsync());
        OpenApiStandard.Violations(document.RootElement).ShouldBeEmpty();
    }

    private Task<IAlbaHost> StartAsync() =>
        AlbaHost.For<Program>(builder =>
            builder
                .UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
                .UseSetting("Nats:Url", nats.Url)
        );

    private sealed record CountryNoted(CountryCode Country);
}
