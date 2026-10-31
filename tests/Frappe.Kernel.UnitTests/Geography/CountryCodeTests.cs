using Frappe.Kernel.Geography;

namespace Frappe.Kernel.UnitTests.Geography;

public sealed class CountryCodeTests
{
    [Theory]
    [InlineData("AR", "AR")]
    [InlineData("ar", "AR")]
    [InlineData(" Ar ", "AR")]
    public void Two_ASCII_letters_are_accepted_in_upper_case(string raw, string expected)
    {
        ErrorOr<CountryCode> result = CountryCode.Create(raw);

        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("A")]
    [InlineData("ARG")]
    [InlineData("A1")]
    [InlineData("A-")]
    [InlineData("ÄR")]
    public void A_value_that_is_not_two_ASCII_letters_is_rejected(string? raw)
    {
        ErrorOr<CountryCode> result = CountryCode.Create(raw);

        result.FirstError.Code.ShouldBe("kernel.country_code.invalid");
    }

    [Fact]
    public void Codes_differing_only_in_case_are_equal()
    {
        CountryCode lower = CountryCode.Create("ar").Value;

        CountryCode upper = CountryCode.Create("AR").Value;

        lower.ShouldBe(upper);
    }

    [Fact]
    public void The_text_form_is_the_code()
    {
        CountryCode code = CountryCode.Create("ar").Value;

        string text = code.ToString();

        text.ShouldBe("AR");
    }
}
