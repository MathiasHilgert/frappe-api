using Frappe.Kernel.Geography;

namespace Frappe.Kernel.UnitTests.Geography;

public sealed class PostalCodeTests
{
    [Theory]
    [InlineData("C1425DQF", "C1425DQF")]
    [InlineData("c1425dqf", "C1425DQF")]
    [InlineData("1425", "1425")]
    [InlineData(" sw1a   1aa ", "SW1A 1AA")]
    [InlineData("SW1A\t1AA", "SW1A 1AA")]
    [InlineData("12345-6789", "12345-6789")]
    [InlineData("ABCDEFGHIJ123456", "ABCDEFGHIJ123456")]
    public void Letters_digits_spaces_and_hyphens_are_accepted_upper_cased_with_whitespace_collapsed(
        string raw,
        string expected
    )
    {
        ErrorOr<PostalCode> result = PostalCode.Create(raw);

        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C1425#")]
    [InlineData("12_34")]
    [InlineData("CÓDIGO")]
    [InlineData("ABCDEFGHIJ1234567")]
    public void An_empty_too_long_or_out_of_alphabet_value_is_rejected(string? raw)
    {
        ErrorOr<PostalCode> result = PostalCode.Create(raw);

        result.FirstError.Code.ShouldBe("kernel.postal_code.invalid");
    }

    [Fact]
    public void Codes_differing_only_in_case_and_spacing_are_equal()
    {
        PostalCode loose = PostalCode.Create(" sw1a  1aa").Value;

        PostalCode canonical = PostalCode.Create("SW1A 1AA").Value;

        loose.ShouldBe(canonical);
    }

    [Fact]
    public void The_text_form_is_the_code()
    {
        PostalCode code = PostalCode.Create("c1425dqf").Value;

        string text = code.ToString();

        text.ShouldBe("C1425DQF");
    }
}
