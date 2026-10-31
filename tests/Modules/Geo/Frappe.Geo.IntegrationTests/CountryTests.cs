using System.Text.Json;
using Alba;

namespace Frappe.Geo.IntegrationTests;

public sealed class CountryTests(GeoFixture geo)
{
    [Fact]
    public async Task A_country_is_found_by_its_alpha2_code_in_any_case_with_its_codes_currency_and_calling_code()
    {
        (_, JsonElement country) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/countries/ar", 200);

        country.GetProperty("countryCode").GetString().ShouldBe("AR");
        country.GetProperty("alpha3").GetString().ShouldBe("ARG");
        country.GetProperty("numericCode").GetString().ShouldBe("032");
        country.GetProperty("name").GetString().ShouldBe("Argentina");
        country.GetProperty("defaultCurrencyCode").GetString().ShouldBe("ARS");
        country.GetProperty("callingCode").GetString().ShouldBe("54");
        country.GetProperty("status").GetString().ShouldBe("active");
    }

    [Fact]
    public async Task The_calling_code_is_digits_without_a_plus_sign()
    {
        (_, JsonElement country) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/countries/US", 200);

        country.GetProperty("callingCode").GetString().ShouldBe("1");
    }

    [Fact]
    public async Task Antarctica_has_neither_a_calling_code_nor_a_default_currency()
    {
        (_, JsonElement country) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/countries/AQ", 200);

        country.GetProperty("callingCode").ValueKind.ShouldBe(JsonValueKind.Null);
        country.GetProperty("defaultCurrencyCode").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("ARG")]
    public async Task An_unknown_or_malformed_country_code_in_the_path_is_not_found(string code)
    {
        (_, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/countries/{code}",
            404
        );

        GeoHttp
            .FirstError(problem)
            .GetProperty("code")
            .GetString()
            .ShouldBe("geo.country.not_found");
    }

    [Fact]
    public async Task A_malformed_locale_is_rejected_at_its_parameter()
    {
        (_, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/countries?locale=e",
            422
        );

        JsonElement error = GeoHttp.FirstError(problem);
        error.GetProperty("code").GetString().ShouldBe("geo.locale.invalid");
        error.GetProperty("pointer").GetString().ShouldBe("/locale");
    }

    [Fact]
    public async Task Without_a_locale_names_are_english_and_the_response_says_so()
    {
        (IScenarioResult result, JsonElement country) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/countries/DE",
            200
        );

        country.GetProperty("name").GetString().ShouldBe("Germany");
        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe("en");
    }

    [Theory]
    [InlineData("es-ar", "Alemania", "es")]
    [InlineData("pt", "Alemanha", "pt")]
    [InlineData("fr", "Germany", "en")]
    public async Task A_name_follows_the_locale_fallback_chain_and_the_response_names_the_locale_it_is_in(
        string locale,
        string name,
        string contentLanguage
    )
    {
        (IScenarioResult result, JsonElement country) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/countries/DE?locale={locale}",
            200
        );

        country.GetProperty("name").GetString().ShouldBe(name);
        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe(contentLanguage);
    }

    [Fact]
    public async Task A_list_names_the_locale_its_names_resolved_to()
    {
        (IScenarioResult result, _) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/countries?locale=fr",
            200
        );

        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe("en");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("es-AR")]
    public async Task Countries_are_listed_by_their_spanish_name_for_a_spanish_locale(string locale)
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/countries?locale={locale}",
            200
        );

        string[] codes = GeoHttp.Codes(page, "countryCode");
        Array.IndexOf(codes, "DE").ShouldBeLessThan(Array.IndexOf(codes, "AR"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public async Task Countries_are_listed_by_their_english_name_for_an_english_or_unseeded_locale(
        string locale
    )
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/countries?locale={locale}",
            200
        );

        string[] codes = GeoHttp.Codes(page, "countryCode");
        Array.IndexOf(codes, "AR").ShouldBeLessThan(Array.IndexOf(codes, "DE"));
    }

    [Fact]
    public async Task The_country_list_is_one_page_of_every_active_country()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/countries", 200);

        page.GetProperty("items").GetArrayLength().ShouldBe(249);
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
