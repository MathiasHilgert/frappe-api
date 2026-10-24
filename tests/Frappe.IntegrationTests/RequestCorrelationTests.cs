using System.Text.Json;
using Shouldly;

namespace Frappe.IntegrationTests;

public sealed class RequestCorrelationTests(ApiFixture api)
{
    private const string Header = "X-Request-Id";

    [Fact]
    public async Task ValidIncomingId_IsEchoed()
    {
        var result = await api.Host.Scenario(s =>
        {
            s.Get.Url("/health/live");
            s.WithRequestHeader(Header, "abc-123_DEF.9");
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.Headers[Header].ToString().ShouldBe("abc-123_DEF.9");
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("has/slash")]
    public async Task InvalidIncomingId_IsReplacedByGeneratedV7(string incoming)
    {
        var id = await GetEchoedId(incoming);

        id.ShouldNotBe(incoming, StringComparer.Ordinal);
        AssertV7(id);
    }

    [Fact]
    public async Task TooLongIncomingId_IsReplacedByGeneratedV7()
    {
        var incoming = new string('a', 129);
        var id = await GetEchoedId(incoming);

        id.ShouldNotBe(incoming, StringComparer.Ordinal);
        AssertV7(id);
    }

    [Fact]
    public async Task MissingId_GeneratesV7()
    {
        AssertV7(await GetEchoedId(null));
    }

    [Fact]
    public async Task UnauthorizedProblemResponse_CarriesRequestId()
    {
        var result = await api.Host.Scenario(s =>
        {
            s.Get.Url($"/v1/stream?businessId={Guid.CreateVersion7()}");
            s.WithRequestHeader(Header, "corr-401");
            s.StatusCodeShouldBe(401);
        });

        result.Context.Response.Headers[Header].ToString().ShouldBe("corr-401");
        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("requestId").GetString().ShouldBe("corr-401");
    }

    private async Task<string> GetEchoedId(string? incoming)
    {
        var result = await api.Host.Scenario(s =>
        {
            s.Get.Url("/health/live");
            if (incoming is not null)
                s.WithRequestHeader(Header, incoming);
            s.StatusCodeShouldBe(200);
        });
        return result.Context.Response.Headers[Header].ToString();
    }

    private static void AssertV7(string id)
    {
        Guid.TryParse(id, out var guid).ShouldBeTrue($"'{id}' is not a Guid");
        guid.Version.ShouldBe(7);
    }
}
