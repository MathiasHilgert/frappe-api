using Frappe.Kernel.Geography;

namespace Frappe.Kernel.UnitTests.Geography;

public sealed class TimeZoneIdTests
{
    [Theory]
    [InlineData("America/Argentina/Cordoba", "America/Argentina/Cordoba")]
    [InlineData("america/argentina/cordoba", "America/Argentina/Cordoba")]
    [InlineData(" Europe/Madrid ", "Europe/Madrid")]
    public void A_tzdb_id_is_accepted_in_its_canonical_case(string raw, string expected)
    {
        ErrorOr<TimeZoneId> result = TimeZoneId.Create(raw);

        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Europe/Oslo")]
    [InlineData("Atlantic/Reykjavik")]
    [InlineData("Europe/Amsterdam")]
    [InlineData("Asia/Kuala_Lumpur")]
    [InlineData("Europe/Kiev")]
    public void A_linked_id_keeps_its_own_id(string raw)
    {
        ErrorOr<TimeZoneId> result = TimeZoneId.Create(raw);

        result.Value.Value.ShouldBe(raw);
    }

    [Fact]
    public void A_lower_case_id_is_stored_with_its_tzdb_casing()
    {
        ErrorOr<TimeZoneId> result = TimeZoneId.Create("america/argentina/buenos_aires");

        result.Value.Value.ShouldBe("America/Argentina/Buenos_Aires");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("America/Nowhere")]
    [InlineData("-03:00")]
    public void A_value_that_is_not_a_tzdb_id_is_rejected(string? raw)
    {
        ErrorOr<TimeZoneId> result = TimeZoneId.Create(raw);

        result.FirstError.Code.ShouldBe("kernel.time_zone_id.invalid");
    }

    [Fact]
    public void A_linked_id_is_not_equal_to_its_target()
    {
        TimeZoneId oslo = TimeZoneId.Create("Europe/Oslo").Value;

        TimeZoneId berlin = TimeZoneId.Create("Europe/Berlin").Value;

        oslo.ShouldNotBe(berlin);
    }

    [Fact]
    public void The_text_form_is_the_id()
    {
        TimeZoneId id = TimeZoneId.Create("america/argentina/cordoba").Value;

        string text = id.ToString();

        text.ShouldBe("America/Argentina/Cordoba");
    }
}
