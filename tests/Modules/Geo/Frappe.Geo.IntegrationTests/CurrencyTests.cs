using System.Text.Json;

namespace Frappe.Geo.IntegrationTests;

public sealed class CurrencyTests(GeoFixture geo)
{
    [Fact]
    public async Task Retired_currencies_are_hidden_from_the_list()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/currencies", 200);

        string[] codes = GeoHttp.Codes(page, "currencyCode");
        codes.Contains("ARS", StringComparer.Ordinal).ShouldBeTrue();
        codes.Contains("BGN", StringComparer.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task A_currency_carries_its_numeric_code_minor_unit_and_localized_name()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/currencies?locale=es",
            200
        );

        JsonElement peso = GeoHttp.Item(page, "currencyCode", "ARS");
        peso.GetProperty("numericCode").GetString().ShouldBe("032");
        peso.GetProperty("minorUnit").GetInt32().ShouldBe(2);
        peso.GetProperty("name").GetString().ShouldBe("peso argentino");
    }

    [Fact]
    public async Task A_currency_without_a_minor_unit_lists_it_as_null()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/currencies", 200);

        JsonElement gold = GeoHttp.Item(page, "currencyCode", "XAU");
        gold.GetProperty("minorUnit").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("es", "USD", "EUR")]
    [InlineData("en", "EUR", "USD")]
    public async Task Currencies_are_listed_by_their_name_in_the_requested_locale(
        string locale,
        string first,
        string second
    )
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            $"/v1/geo/currencies?locale={locale}",
            200
        );

        string[] codes = GeoHttp.Codes(page, "currencyCode");
        Array.IndexOf(codes, first).ShouldBeLessThan(Array.IndexOf(codes, second));
    }
}
