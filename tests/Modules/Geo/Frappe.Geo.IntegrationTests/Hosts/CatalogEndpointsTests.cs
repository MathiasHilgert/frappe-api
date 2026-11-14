using System.Net;
using Frappe.Geo.IntegrationTests.Persistence;

namespace Frappe.Geo.IntegrationTests.Hosts;

/// <summary>The country and currency endpoints over the seeded CLDR catalog. Read-only: one database for the class.</summary>
[ClassDataSource<GeoDatabase, ValkeyCache>(Shared = [SharedType.PerClass, SharedType.PerTestSession])]
public sealed class CatalogEndpointsTests(GeoDatabase database, ValkeyCache valkey)
{
    [Test]
    public async Task Every_ISO_country_is_listed_in_the_requested_language()
    {
        var answer = await GeoApi.GetAsync(database, valkey, "/v1/geo/countries", "es-AR");

        var countries = answer.Body.GetProperty("countries").EnumerateArray().ToArray();
        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(countries.Length).IsEqualTo(249);
        await Assert.That(countries.Single(c => c.GetProperty("code").GetString() == "DE").GetProperty("name").GetString()).IsEqualTo("Alemania");
        // Most names are common Spanish; the few CLDR writes differently for Argentina are served in es-AR.
        await Assert.That(answer.ContentLanguage).IsEqualTo("es, es-AR");
    }

    [Test]
    public async Task A_country_answers_its_codes_and_currencies_and_a_lower_case_code_is_accepted()
    {
        var answer = await GeoApi.GetAsync(database, valkey, "/v1/geo/countries/pa", "pt-PT");

        await Assert.That(answer.Status).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(answer.Body.GetProperty("code").GetString()).IsEqualTo("PA");
        await Assert.That(answer.Body.GetProperty("alpha3").GetString()).IsEqualTo("PAN");
        await Assert.That(answer.Body.GetProperty("numeric").GetString()).IsEqualTo("591");
        await Assert.That(answer.Body.GetProperty("currencies").EnumerateArray().Select(c => c.GetString()!)).IsEquivalentTo(["PAB", "USD"]);
        await Assert.That(answer.ContentLanguage).IsEqualTo("pt-BR");
    }

    [Test]
    [Arguments("/v1/geo/countries/ARG", 400, "geo.country_code.invalid_format")]
    [Arguments("/v1/geo/countries/QX", 404, "geo.country.not_found")]
    [Arguments("/v1/geo/countries/QX/currencies", 404, "geo.country.not_found")]
    [Arguments("/v1/geo/currencies/AR", 400, "geo.currency_code.invalid_format")]
    [Arguments("/v1/geo/currencies/ARA", 404, "geo.currency.not_found")]
    public async Task Errors_are_problem_details_with_a_stable_code_and_a_translated_message(string path, int status, string code)
    {
        var answer = await GeoApi.GetAsync(database, valkey, path, "es");

        var error = answer.Body.GetProperty("errors")[0];
        await Assert.That((int)answer.Status).IsEqualTo(status);
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo(code);
        await Assert.That(error.GetProperty("message").GetString()).IsNotNull().And.IsNotEmpty();
    }

    [Test]
    public async Task The_peso_is_written_with_its_local_symbol_only_in_Argentina()
    {
        var argentina = await GeoApi.GetAsync(database, valkey, "/v1/geo/currencies/ARS", "es-AR");
        var mexico = await GeoApi.GetAsync(database, valkey, "/v1/geo/currencies/ars", "es-MX");

        await Assert.That(argentina.Body.GetProperty("symbol").GetString()).IsEqualTo("$");
        await Assert.That(argentina.Body.GetProperty("name").GetString()).IsEqualTo("peso argentino");
        await Assert.That(argentina.Body.GetProperty("minorUnitDigits").GetInt32()).IsEqualTo(2);
        await Assert.That(argentina.ContentLanguage).IsEqualTo("es-AR");
        await Assert.That(mexico.Body.GetProperty("symbol").GetString()).IsNotEqualTo("$");
    }

    [Test]
    public async Task The_currencies_of_a_country_and_the_whole_catalog_are_served()
    {
        var argentina = await GeoApi.GetAsync(database, valkey, "/v1/geo/countries/AR/currencies", "en");
        var all = await GeoApi.GetAsync(database, valkey, "/v1/geo/currencies", "en");

        await Assert.That(argentina.Body.GetProperty("currencies").EnumerateArray().Select(c => c.GetProperty("code").GetString()!)).IsEquivalentTo(["ARS"]);
        await Assert.That(all.Body.GetProperty("currencies").GetArrayLength()).IsEqualTo(153);
    }
}
