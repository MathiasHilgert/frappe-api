using Frappe.Kernel.Geography;

namespace Frappe.Kernel.UnitTests.Geography;

public sealed class SubdivisionCodeTests
{
    [Theory]
    [InlineData("AR-X", "AR-X")]
    [InlineData("ar-c", "AR-C")]
    [InlineData(" gb-eng ", "GB-ENG")]
    [InlineData("FR-75C", "FR-75C")]
    public void A_country_code_a_hyphen_and_one_to_three_letters_or_digits_are_accepted_in_upper_case(
        string raw,
        string expected
    )
    {
        ErrorOr<SubdivisionCode> result = SubdivisionCode.Create(raw);

        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("AR")]
    [InlineData("AR-")]
    [InlineData("AR-ABCD")]
    [InlineData("A-X")]
    [InlineData("ARG-X")]
    [InlineData("AR_X")]
    [InlineData("1R-X")]
    [InlineData("AR-X-1")]
    [InlineData("AR-Ñ")]
    public void A_malformed_value_is_rejected(string? raw)
    {
        ErrorOr<SubdivisionCode> result = SubdivisionCode.Create(raw);

        result.FirstError.Code.ShouldBe("kernel.subdivision_code.invalid");
    }

    [Fact]
    public void The_country_is_the_code_prefix()
    {
        SubdivisionCode code = SubdivisionCode.Create("ar-x").Value;

        CountryCode country = code.Country;

        country.ShouldBe(CountryCode.Create("AR").Value);
    }

    [Fact]
    public void Codes_differing_only_in_case_are_equal()
    {
        SubdivisionCode lower = SubdivisionCode.Create("ar-x").Value;

        SubdivisionCode upper = SubdivisionCode.Create("AR-X").Value;

        lower.ShouldBe(upper);
    }

    [Fact]
    public void The_text_form_is_the_code()
    {
        SubdivisionCode code = SubdivisionCode.Create("ar-x").Value;

        string text = code.ToString();

        text.ShouldBe("AR-X");
    }
}
