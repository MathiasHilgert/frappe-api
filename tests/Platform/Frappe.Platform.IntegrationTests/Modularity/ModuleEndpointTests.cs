using Alba;
using Frappe.Platform.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.IntegrationTests.Modularity;

public sealed class ModuleEndpointTests
{
    [Theory]
    [InlineData("api")]
    [InlineData("all")]
    public async Task A_module_endpoint_answers_under_v1_and_its_prefix_in_the_business_roles(
        string role
    )
    {
        await using IAlbaHost host = await StartAsync(role);

        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/pings/latest");
            scenario.StatusCodeShouldBeOk();
            scenario.ContentShouldBe("pong");
        });
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("scheduler")]
    public async Task A_module_endpoint_is_not_mapped_in_the_other_roles(string role)
    {
        await using IAlbaHost host = await StartAsync(role);

        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/pings/latest");
            scenario.StatusCodeShouldBe(404);
        });
    }

    [Theory]
    [InlineData("api")]
    [InlineData("all")]
    [InlineData("worker")]
    [InlineData("scheduler")]
    public async Task Health_answers_in_every_role_next_to_the_modules(string role)
    {
        await using IAlbaHost host = await StartAsync(role);

        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/health/live");
            scenario.StatusCodeShouldBeOk();
        });
        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/health/ready");
            scenario.StatusCodeShouldBeOk();
        });
    }

    // Health and the modules without persistence: the platform endpoints as Program maps them, no database needed.
    private static async Task<IAlbaHost> StartAsync(string role)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        builder.Configuration["Platform:Role"] = role;
        builder.AddHealth();
        builder.AddFrappeModules(new PingModule());
        return await AlbaHost.For(builder, static app => app.MapFrappeEndpoints());
    }
}
