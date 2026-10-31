using System.Text.Json;
using Alba;

namespace Frappe.Geo.IntegrationTests;

public sealed class LocaleTests(GeoFixture geo)
{
    [Fact]
    public async Task A_locale_carries_its_tag_language_direction_endonym_and_name()
    {
        (IScenarioResult result, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/locales?locale=es",
            200
        );

        JsonElement argentina = GeoHttp.Item(page, "localeTag", "es-AR");
        argentina.GetProperty("languageCode").GetString().ShouldBe("es");
        argentina.GetProperty("textDirection").GetString().ShouldBe("ltr");
        argentina.GetProperty("endonym").GetString().ShouldBe("español (Argentina)");
        argentina.GetProperty("name").GetString().ShouldBe("español (Argentina)");
        GeoHttp
            .Item(page, "localeTag", "ar")
            .GetProperty("textDirection")
            .GetString()
            .ShouldBe("rtl");
        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe("es");
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Every_modern_cldr_locale_is_listed()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/locales", 200);

        string[] tags = GeoHttp.Codes(page, "localeTag");
        tags.Length.ShouldBe(429);
        tags.Contains("zh-Hant-HK", StringComparer.Ordinal).ShouldBeTrue();
        tags.Contains("ca-ES-valencia", StringComparer.Ordinal).ShouldBeTrue();
        tags.Contains("und", StringComparer.Ordinal).ShouldBeFalse();
    }

    [Theory]
    [InlineData("en", "en", "de")]
    [InlineData("es", "de", "en")]
    public async Task Locales_are_listed_by_their_name_in_the_served_locale(
        string locale,
        string first,
        string second
    )
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/locales?locale={locale}",
            200
        );

        // English before German in English; alemán before inglés in Spanish.
        string[] tags = GeoHttp.Codes(page, "localeTag");
        Array.IndexOf(tags, first).ShouldBeLessThan(Array.IndexOf(tags, second));
    }
}
