using Alba;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.IntegrationTests.Persistence;

public sealed class DatabaseHealthTests(AppFixture app)
{
    private const string Password = "S3cr3tPw";

    // Nothing listens on port 1, so the connection is refused at once.
    private const string UnreachableDatabase =
        $"Host=127.0.0.1;Port=1;Username=frappe;Password={Password};Database=frappe;Timeout=2";

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health/startup")]
    public async Task Readiness_and_startup_are_healthy_when_the_database_answers(string url)
    {
        await app.Host.Scenario(scenario =>
        {
            scenario.Get.Url(url);
            scenario.StatusCodeShouldBeOk();
        });
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health/startup")]
    public async Task Readiness_and_startup_are_unhealthy_when_the_database_is_unreachable(
        string url
    )
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(static builder =>
            builder.UseSetting("ConnectionStrings:Frappe", UnreachableDatabase)
        );

        await host.Scenario(scenario =>
        {
            scenario.Get.Url(url);
            scenario.StatusCodeShouldBe(503);
        });
    }

    [Fact]
    public async Task Liveness_stays_healthy_when_the_database_is_unreachable()
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(static builder =>
            builder.UseSetting("ConnectionStrings:Frappe", UnreachableDatabase)
        );

        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/health/live");
            scenario.StatusCodeShouldBeOk();
        });
    }

    [Fact]
    public async Task Readiness_reports_only_the_status_when_the_database_is_unreachable()
    {
        await using IAlbaHost host = await AlbaHost.For<Program>(static builder =>
            builder.UseSetting("ConnectionStrings:Frappe", UnreachableDatabase)
        );

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/health/ready");
            scenario.StatusCodeShouldBe(503);
        });

        string body = await result.ReadAsTextAsync();
        body.ShouldBe("Unhealthy");
        body.ShouldNotContain(Password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_connection_string_stops_the_host_from_starting(
        string? connectionString
    )
    {
        using IHost host = BuildPlatform(connectionString);

        OptionsValidationException error = await Should.ThrowAsync<OptionsValidationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken)
        );

        error.Message.ShouldContain("Frappe");
    }

    [Fact]
    public async Task A_malformed_connection_string_stops_the_host_without_revealing_it()
    {
        const string Malformed = $"Host=127.0.0.1;Password={Password};Bogus Key=1";

        using IHost host = BuildPlatform(Malformed);

        OptionsValidationException error = await Should.ThrowAsync<OptionsValidationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken)
        );

        error.Message.ShouldContain("ConnectionStrings:Frappe");
        error.Message.ShouldNotContain(Password);
    }

    // Startup failures are asserted on a host built here, as Program composes it: through
    // WebApplicationFactory the app thread can dispose the host first (dotnet/aspnetcore#69684).
    private static IHost BuildPlatform(string? connectionString)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        builder.Configuration["ConnectionStrings:Frappe"] = connectionString;
        builder.AddFrappePlatform();
        return builder.Build();
    }
}
