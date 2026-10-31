using Alba;
using Frappe.Geo.Infrastructure;
using Frappe.TestUtilities;
using Microsoft.Extensions.Options;

namespace Frappe.Geo.IntegrationTests;

public sealed class GeoOptionsTests(AppFixture app)
{
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("366.00:00:00")]
    public async Task An_out_of_range_catalogue_max_age_fails_the_start(string maxAge)
    {
        OptionsValidationException exception = await Should.ThrowAsync<OptionsValidationException>(
            async () =>
            {
                await using IAlbaHost host = await FrappeTestHost.StartAsync(
                    app.AppConnectionString,
                    [new GeoModule()],
                    configure: builder =>
                        builder.Configuration[$"{GeoOptions.SectionName}:CatalogueMaxAge"] = maxAge
                );
            }
        );

        exception.OptionsType.ShouldBe(typeof(GeoOptions));
        exception.Message.ShouldContain(nameof(GeoOptions.CatalogueMaxAge));
    }

    [Fact]
    public async Task A_leftover_timezones_max_age_key_fails_the_start()
    {
        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(
            async () =>
            {
                await using IAlbaHost host = await FrappeTestHost.StartAsync(
                    app.AppConnectionString,
                    [new GeoModule()],
                    configure: static builder =>
                        builder.Configuration[$"{GeoOptions.SectionName}:TimezonesMaxAge"] =
                            "1.00:00:00"
                );
            }
        );

        exception.Message.ShouldContain("TimezonesMaxAge");
    }
}
