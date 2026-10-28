using Alba;
using Frappe.Platform.Health;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

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
        await using IAlbaHost host = await StartWithUnreachableDatabaseAsync();

        await host.Scenario(scenario =>
        {
            scenario.Get.Url(url);
            scenario.StatusCodeShouldBe(503);
        });
    }

    [Fact]
    public async Task Liveness_stays_healthy_when_the_database_is_unreachable()
    {
        await using IAlbaHost host = await StartWithUnreachableDatabaseAsync();

        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/health/live");
            scenario.StatusCodeShouldBeOk();
        });
    }

    [Fact]
    public async Task Readiness_reports_only_the_status_when_the_database_is_unreachable()
    {
        await using IAlbaHost host = await StartWithUnreachableDatabaseAsync();

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

    // The bus needs its store to start, so the full platform cannot start without the database (asserted
    // below); the health checks are proven on persistence alone, which starts and reports the outage.
    [Fact]
    public async Task The_platform_does_not_start_while_the_database_is_unreachable()
    {
        using IHost host = BuildPlatform(UnreachableDatabase);

        // Wolverine's store check fails first and wraps the connection failure in an AggregateException.
        Exception error = await Should.ThrowAsync<Exception>(() =>
            host.StartAsync(TestContext.Current.CancellationToken)
        );

        (error is AggregateException all ? all.Flatten().InnerExceptions : [error]).ShouldContain(
            static inner => inner is NpgsqlException
        );
        error.ToString().ShouldNotContain(Password);
    }

    private static async Task<IAlbaHost> StartWithUnreachableDatabaseAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Configuration["ConnectionStrings:Frappe"] = UnreachableDatabase;
        builder.AddHealth();
        builder.AddPersistence();
        return await AlbaHost.For(builder, static app => app.MapHealthEndpoints());
    }

    // Startup failures are asserted on a host built here, as Program composes it: through
    // WebApplicationFactory the app thread can dispose the host first (dotnet/aspnetcore#69684).
    private static IHost BuildPlatform(string? connectionString)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        builder.Configuration["ConnectionStrings:Frappe"] = connectionString;
        // The database fails the start first: Wolverine registers its node before any transport connects.
        builder.Configuration["Nats:Url"] = "nats://127.0.0.1:1";
        builder.AddFrappePlatform();
        return builder.Build();
    }
}
