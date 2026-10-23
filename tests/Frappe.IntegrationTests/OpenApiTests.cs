using System.Text.Json;
using Shouldly;

namespace Frappe.IntegrationTests;

public sealed class OpenApiTests(ApiFixture api)
{
    [Fact]
    public async Task OpenApiDocument_InDevelopment_DescribesTheApi()
    {
        var result = await api.Host.Scenario(s =>
        {
            s.Get.Url("/openapi/v1.json");
            s.StatusCodeShouldBe(200);
        });

        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("info").GetProperty("title").GetString().ShouldBe("Frappé API");
        body.GetProperty("paths").TryGetProperty("/v1/stream", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task ScalarReference_InDevelopment_ServesHtml()
    {
        var result = await api.Host.Scenario(s =>
        {
            s.Get.Url("/scalar/v1");
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.ContentType.ShouldNotBeNull().ShouldStartWith("text/html");
        (await result.ReadAsTextAsync()).ShouldContain("<html", Case.Insensitive);
    }

    [Fact]
    public async Task OpenApiDocument_InProduction_IsNotExposed()
    {
        // Authenticated, so the 404 proves the route is absent: anonymous callers get 401 for any unknown route.
        await using var host = await api.CreateHostAsync(services => services.AuthenticateEveryone(), "Production");

        await host.Scenario(s =>
        {
            s.Get.Url("/openapi/v1.json");
            s.StatusCodeShouldBe(404);
        });
    }
}
