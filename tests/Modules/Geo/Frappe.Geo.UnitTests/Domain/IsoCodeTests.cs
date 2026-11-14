using Frappe.Geo.Domain;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Currencies;

namespace Frappe.Geo.UnitTests.Domain;

public sealed class IsoCodeTests
{
    [Test]
    [Arguments("AR", "AR")]
    [Arguments(" ar ", "AR")]
    public async Task A_country_code_is_trimmed_and_upper_cased(string raw, string expected) =>
        await Assert.That(CountryCode.Create(raw).Value.Value).IsEqualTo(expected);

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("ARG")]
    [Arguments("A1")]
    [Arguments("ſe")]
    public async Task Anything_else_is_not_a_country_code(string? raw)
    {
        var result = CountryCode.Create(raw);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors.Single().Code).IsEqualTo(GeoErrorCodes.CountryCodeInvalidFormat);
    }

    [Test]
    [Arguments("ARS", "ARS")]
    [Arguments(" usd", "USD")]
    public async Task A_currency_code_is_trimmed_and_upper_cased(string raw, string expected) =>
        await Assert.That(CurrencyCode.Create(raw).Value.Value).IsEqualTo(expected);

    [Test]
    [Arguments(null)]
    [Arguments("AR")]
    [Arguments("AR5")]
    [Arguments("ARSS")]
    public async Task Anything_else_is_not_a_currency_code(string? raw)
    {
        var result = CurrencyCode.Create(raw);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors.Single().Code).IsEqualTo(GeoErrorCodes.CurrencyCodeInvalidFormat);
    }
}
