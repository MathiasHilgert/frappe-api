using Alba;
using Frappe.Testing;
using Microsoft.Extensions.Options;

namespace Frappe.Host.Tests;

public sealed class HostRoleTests(FrappeDatabase database)
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
            builder.UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
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
        InvalidOperationException error = await Should.ThrowAsync<InvalidOperationException>(() =>
            AlbaHost.For<Program>(builder =>
                builder
                    .UseSetting("Platform:Role", "web")
                    .UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
            )
        );

        error.Message.ShouldContain("Platform:Role");
    }

    [Fact]
    public async Task A_numeric_role_outside_the_defined_roles_stops_the_host_from_starting()
    {
        OptionsValidationException error = await Should.ThrowAsync<OptionsValidationException>(() =>
            AlbaHost.For<Program>(builder =>
                builder
                    .UseSetting("Platform:Role", "7")
                    .UseSetting("ConnectionStrings:Frappe", database.AppConnectionString)
            )
        );

        error.Message.ShouldContain("Role");
    }
}
