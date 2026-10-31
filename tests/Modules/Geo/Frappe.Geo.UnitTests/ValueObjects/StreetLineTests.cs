using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.UnitTests.ValueObjects;

public sealed class StreetLineTests
{
    [Theory]
    [InlineData("Av. Corrientes 1234", "Av. Corrientes 1234")]
    [InlineData("  Boulevard España  ", "Boulevard España")]
    [InlineData("Avenida Corrientes", "Avenida Corrientes")]
    [InlineData("Ruta 9 km 563", "Ruta 9 km 563")]
    public void A_street_line_is_trimmed_and_needs_no_house_number(string raw, string expected)
    {
        ErrorOr<StreetLine> result = StreetLine.Create(raw);

        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Calle\u0007 12")]
    [InlineData("Calle\n12")]
    [InlineData("Calle\u200B12")]
    [InlineData("Calle\u202E12")]
    [InlineData("Av\u2028Mayo")]
    [InlineData("Av\u2029Mayo")]
    public void An_empty_control_format_or_separator_character_street_line_is_rejected(string? raw)
    {
        ErrorOr<StreetLine> result = StreetLine.Create(raw);

        result.FirstError.Code.ShouldBe("geo.address.street_line_invalid");
    }

    [Fact]
    public void A_street_line_is_at_most_200_characters_after_trimming()
    {
        ErrorOr<StreetLine> longest = StreetLine.Create(" " + new string('a', 200) + " ");
        ErrorOr<StreetLine> tooLong = StreetLine.Create(new string('a', 201));

        longest.Value.Value.Length.ShouldBe(200);
        tooLong.FirstError.Code.ShouldBe("geo.address.street_line_invalid");
    }

    // Facts, not theory data: theory data travels as UTF-8, which turns a lone surrogate into U+FFFD.
    [Fact]
    public void A_lone_high_surrogate_is_rejected()
    {
        ErrorOr<StreetLine> result = StreetLine.Create("\uD800");

        result.FirstError.Code.ShouldBe("geo.address.street_line_invalid");
    }

    [Fact]
    public void A_lone_low_surrogate_inside_the_line_is_rejected()
    {
        ErrorOr<StreetLine> result = StreetLine.Create("Av \uDC00 1");

        result.FirstError.Code.ShouldBe("geo.address.street_line_invalid");
    }

    // A character outside the Basic Multilingual Plane is one valid surrogate pair, not a control character.
    [Fact]
    public void A_symbol_written_as_a_surrogate_pair_is_kept()
    {
        ErrorOr<StreetLine> result = StreetLine.Create("Pizzería \U0001F355 1");

        result.Value.Value.ShouldBe("Pizzería \U0001F355 1");
    }

    [Fact]
    public void The_text_form_is_the_street_line()
    {
        StreetLine line = StreetLine.Create(" Calle 1 ").Value;

        string text = line.ToString();

        text.ShouldBe("Calle 1");
    }
}
