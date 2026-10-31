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
    [InlineData("Calle​12")]
    [InlineData("Calle‮12")]
    public void An_empty_control_or_format_character_street_line_is_rejected(string? raw)
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

    [Fact]
    public void The_text_form_is_the_street_line()
    {
        StreetLine line = StreetLine.Create(" Calle 1 ").Value;

        string text = line.ToString();

        text.ShouldBe("Calle 1");
    }
}
