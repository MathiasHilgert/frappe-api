using Alba;
using Frappe.Geo.Infrastructure;
using Frappe.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Geo.IntegrationTests.Timezones;

public sealed class TimezoneHealthTests(AppFixture app)
{
    private const string Check = "geo.timezones";

    [Fact]
    public async Task The_catalogue_is_unhealthy_while_it_is_empty()
    {
        await using var unseeded = FrappeDatabase.WithoutSeed();
        await unseeded.InitializeAsync();
        await using IAlbaHost host = await FrappeTestHost.StartAsync(
            unseeded.AppConnectionString,
            [new GeoModule()],
            nats: NatsScope.On(app.Nats)
        );

        HealthStatus status = await CheckAsync(host);

        status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task The_catalogue_is_healthy_once_seeded()
    {
        HealthStatus status = await CheckAsync(app.Host);

        status.ShouldBe(HealthStatus.Healthy);
    }

    private static async Task<HealthStatus> CheckAsync(IAlbaHost host)
    {
        HealthReport report = await host
            .Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                static registration =>
                    string.Equals(registration.Name, Check, StringComparison.Ordinal),
                TestContext.Current.CancellationToken
            );
        return report.Entries[Check].Status;
    }
}
