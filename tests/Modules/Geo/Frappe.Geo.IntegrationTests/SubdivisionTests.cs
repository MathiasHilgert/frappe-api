using System.Text.Json;
using Alba;

namespace Frappe.Geo.IntegrationTests;

public sealed class SubdivisionTests(GeoFixture geo)
{
    [Fact]
    public async Task Argentina_has_its_24_provinces_with_code_and_name_only()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/countries/ar/subdivisions",
            200
        );

        page.GetProperty("items").GetArrayLength().ShouldBe(24);
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
        JsonElement cordoba = GeoHttp.Item(page, "subdivisionCode", "AR-X");
        cordoba.GetProperty("name").GetString().ShouldBe("Córdoba");
        cordoba
            .EnumerateObject()
            .Select(static member => member.Name)
            .ShouldBe(["subdivisionCode", "name"]);
    }

    [Fact]
    public async Task A_portuguese_list_names_cordoba_as_iso_codes_does_and_says_it_is_portuguese()
    {
        (IScenarioResult result, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/countries/AR/subdivisions?locale=pt",
            200
        );

        GeoHttp
            .Item(page, "subdivisionCode", "AR-X")
            .GetProperty("name")
            .GetString()
            .ShouldBe("Córdoba");
        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe("pt");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Subdivisions_are_listed_by_their_name_in_the_served_locale(string locale)
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/countries/AR/subdivisions?locale={locale}",
            200
        );

        string[] codes = GeoHttp.Codes(page, "subdivisionCode");
        // Buenos Aires, Catamarca, Chaco, Chubut, Ciudad Autónoma..., Córdoba: culture order, accents ignored.
        codes[..6].ShouldBe(["AR-B", "AR-K", "AR-H", "AR-U", "AR-C", "AR-X"]);
    }

    [Fact]
    public async Task A_country_without_subdivisions_in_the_catalogue_has_an_empty_list()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/countries/DE/subdivisions",
            200
        );

        page.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("ARG")]
    public async Task An_unknown_or_malformed_country_in_the_path_is_not_found(string code)
    {
        (IScenarioResult result, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/countries/{code}/subdivisions",
            404
        );

        GeoHttp
            .FirstError(problem)
            .GetProperty("code")
            .GetString()
            .ShouldBe("geo.country.not_found");
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
    }

    [Fact]
    public async Task A_subdivision_list_is_cacheable_with_the_dataset_tag()
    {
        (IScenarioResult result, _) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/countries/AR/subdivisions",
            200
        );

        result.Context.Response.Headers.ETag.ToString().ShouldStartWith("\"");
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=86400");
    }
}
