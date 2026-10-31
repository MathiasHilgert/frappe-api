using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.UnitTests.ValueObjects;

public sealed class CurrencyCodeTests
{
    [Theory]
    [InlineData("ARS", "ARS")]
    [InlineData("ars", "ARS")]
    [InlineData(" Usd ", "USD")]
    public void Three_ASCII_letters_are_accepted_in_upper_case(string raw, string expected)
    {
        ErrorOr<CurrencyCode> result = CurrencyCode.Create(raw);

        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AR")]
    [InlineData("ARSS")]
    [InlineData("AR5")]
    [InlineData("032")]
    [InlineData("ÅRS")]
    [InlineData("u\u017Fd")]
    [InlineData("\u0131ls")]
    [InlineData("\u212Arw")]
    public void A_value_that_is_not_three_ASCII_letters_is_rejected(string? raw)
    {
        ErrorOr<CurrencyCode> result = CurrencyCode.Create(raw);

        result.FirstError.Code.ShouldBe("geo.currency_code.invalid");
    }

    [Fact]
    public void Codes_differing_only_in_case_are_equal()
    {
        CurrencyCode lower = CurrencyCode.Create("ars").Value;

        CurrencyCode upper = CurrencyCode.Create("ARS").Value;

        lower.ShouldBe(upper);
    }

    [Fact]
    public void The_text_form_is_the_code()
    {
        CurrencyCode code = CurrencyCode.Create("ars").Value;

        string text = code.ToString();

        text.ShouldBe("ARS");
    }
}
