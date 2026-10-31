using Alba;

namespace Frappe.Geo.IntegrationTests;

public sealed class CatalogueCachingTests(GeoFixture geo)
{
    private const string Cacheable = "public, max-age=86400";

    [Fact]
    public async Task A_catalogue_read_carries_the_dataset_tag_and_is_cacheable_for_a_day()
    {
        (IScenarioResult result, _) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/currencies", 200);

        result.Context.Response.Headers.ETag.ToString().ShouldStartWith("\"");
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe(Cacheable);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("W/{0}")]
    [InlineData("\"other\", {0}")]
    [InlineData("*")]
    public async Task A_request_holding_the_dataset_tag_gets_not_modified_with_both_headers(
        string ifNoneMatch
    )
    {
        string tag = await DatasetTagAsync();

        IScenarioResult result = await geo.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/v1/geo/countries");
            scenario.WithRequestHeader(
                "If-None-Match",
                string.Format(System.Globalization.CultureInfo.InvariantCulture, ifNoneMatch, tag)
            );
            scenario.StatusCodeShouldBe(304);
        });

        (await result.ReadAsTextAsync()).ShouldBeEmpty();
        result.Context.Response.Headers.ETag.ToString().ShouldBe(tag);
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe(Cacheable);
    }

    [Fact]
    public async Task A_request_holding_another_tag_gets_the_full_response()
    {
        IScenarioResult result = await geo.Host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/geo/countries");
            scenario.WithRequestHeader("If-None-Match", "\"stale\"");
            scenario.StatusCodeShouldBeOk();
        });

        result.Context.Response.Headers.CacheControl.ToString().ShouldBe(Cacheable);
    }

    [Theory]
    [InlineData("/v1/geo/countries/ZZ", 404)]
    [InlineData("/v1/geo/currencies?locale=e", 422)]
    public async Task An_error_is_never_stored(string url, int status)
    {
        (IScenarioResult result, _) = await GeoHttp.GetAsync(geo.Host, url, status);

        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
        result.Context.Response.Headers.ETag.ToString().ShouldBeEmpty();
    }

    // The tag only stands for a representation the endpoint actually produced, so * cannot hide a missing resource.
    [Theory]
    [InlineData("/v1/geo/countries/ZZ", 404)]
    [InlineData("/v1/geo/time-zones?countryCode=ZZ", 422)]
    public async Task A_conditional_request_for_a_failing_read_gets_the_failure(
        string url,
        int status
    )
    {
        IScenarioResult result = await geo.Host.Scenario(scenario =>
        {
            scenario.Get.Url(url);
            scenario.WithRequestHeader("If-None-Match", "*");
            scenario.StatusCodeShouldBe(status);
        });

        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
    }

    private async Task<string> DatasetTagAsync()
    {
        (IScenarioResult result, _) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/time-zones", 200);
        return result.Context.Response.Headers.ETag.ToString();
    }
}
