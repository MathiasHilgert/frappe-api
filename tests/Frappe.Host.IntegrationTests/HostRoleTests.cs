using Alba;
using Frappe.Platform;
using Frappe.TestUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frappe.Host.IntegrationTests;

public sealed class HostRoleTests(FrappeDatabase database, FrappeNats nats)
{
    [Theory]
    [InlineData("all", "/health/live")]
    [InlineData("api", "/health/live")]
    [InlineData("worker", "/health/live")]
    [InlineData("scheduler", "/health/live")]
    [InlineData("API", "/health/live")]
    [InlineData("all", "/health/ready")]
    [InlineData("api", "/health/ready")]
    [InlineData("worker", "/health/ready")]
    [InlineData("scheduler", "/health/ready")]
    [InlineData("all", "/health/startup")]
    [InlineData("api", "/health/startup")]
    [InlineData("worker", "/health/startup")]
    [InlineData("scheduler", "/health/startup")]
    public async Task Every_role_answers_the_health_endpoints(string role, string url)
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(builder =>
            builder
                .UseSetting("Platform:Role", role)
                .UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
                .UseSetting("Nats:Url", nats.Url)
        );

        await host.Scenario(scenario =>
        {
            scenario.Get.Url(url);
            scenario.StatusCodeShouldBeOk();
        });
    }

    [Fact]
    public async Task The_host_starts_without_a_role_setting()
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(builder =>
            builder
                .UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
                .UseSetting("Nats:Url", nats.Url)
        );

        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/health/live");
            scenario.StatusCodeShouldBeOk();
        });
    }

    [Fact]
    public async Task A_role_name_that_does_not_exist_stops_the_host_from_starting()
    {
        using IHost host = BuildPlatform("web");

        InvalidOperationException error = await Should.ThrowAsync<InvalidOperationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken)
        );

        error.Message.ShouldContain("Platform:Role");
    }

    [Fact]
    public async Task A_numeric_role_outside_the_defined_roles_stops_the_host_from_starting()
    {
        using IHost host = BuildPlatform("7");

        OptionsValidationException error = await Should.ThrowAsync<OptionsValidationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken)
        );

        error.Message.ShouldContain("Role");
    }

    // Startup failures are asserted on a host built here, as Program composes it: through
    // WebApplicationFactory the app thread can dispose the host first (dotnet/aspnetcore#69684).
    private IHost BuildPlatform(string role)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        builder.Configuration["Platform:Role"] = role;
        builder.Configuration["ConnectionStrings:Frappe"] = database.AppConnectionString;
        builder.Configuration["Nats:Url"] = nats.Url;
        builder.AddFrappePlatform();
        return builder.Build();
    }
}
