using Shouldly;

namespace Frappe.IntegrationTests;

public sealed class SecurityTests(ApiFixture api)
{
    private const string UnknownRoute = "/v1/does-not-exist";

    [Fact]
    public async Task UnknownRoute_Anonymous_IsUnauthorizedSoRoutesCannotBeDiscovered()
    {
        await api.Host.Scenario(s =>
        {
            s.Get.Url(UnknownRoute);
            s.StatusCodeShouldBe(401);
        });
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthProbes_Anonymous_StayReachable(string url)
    {
        var result = await api.Host.Scenario(s =>
        {
            s.Get.Url(url);
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task UnknownRoute_Authenticated_IsNotFound()
    {
        await using var host = await api.CreateHostAsync(services => services.AuthenticateEveryone());

        await host.Scenario(s =>
        {
            s.Get.Url(UnknownRoute);
            s.StatusCodeShouldBe(404);
        });
    }
}
