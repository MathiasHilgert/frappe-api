using Alba;
using Frappe.Geo.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Geo.IntegrationTests;

public sealed class QueryCountTests(GeoFixture geo)
{
    [Theory]
    [InlineData("/v1/geo/time-zones")]
    [InlineData("/v1/geo/currencies?locale=es")]
    [InlineData("/v1/geo/countries?locale=pt")]
    [InlineData("/v1/geo/countries/AR")]
    public async Task A_catalogue_read_sends_no_command_to_the_database(string url)
    {
        CommandCounter counter = new();
        await using IAlbaHost host = await geo.StartHostAsync(builder =>
            builder.Services.ConfigureDbContext<GeoDbContext>(options =>
                options.AddInterceptors(counter)
            )
        );
        int afterStart = counter.Count;

        await GeoHttp.GetAsync(host, url, 200);

        counter.Count.ShouldBe(afterStart);
    }

    [Fact]
    public async Task The_counter_sees_the_catalogues_load_at_start()
    {
        CommandCounter counter = new();

        await using IAlbaHost host = await geo.StartHostAsync(builder =>
            builder.Services.ConfigureDbContext<GeoDbContext>(options =>
                options.AddInterceptors(counter)
            )
        );

        counter.Count.ShouldBeGreaterThan(0);
    }
}
