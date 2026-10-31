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
    [InlineData("Europe/Kiev", "Europe/Kyiv")]
    [InlineData("america/buenos_aires", "America/Argentina/Buenos_Aires")]
    public void An_alias_is_stored_as_its_canonical_id(string raw, string expected)
    {
        ErrorOr<TimeZoneId> result = TimeZoneId.Create(raw);

        result.Value.Value.ShouldBe(expected);
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
    public void An_alias_equals_its_canonical_id()
    {
        TimeZoneId alias = TimeZoneId.Create("Europe/Kiev").Value;

        TimeZoneId canonical = TimeZoneId.Create("Europe/Kyiv").Value;

        alias.ShouldBe(canonical);
    }

    [Fact]
    public void The_text_form_is_the_id()
    {
        TimeZoneId id = TimeZoneId.Create("america/argentina/cordoba").Value;

        string text = id.ToString();

        text.ShouldBe("America/Argentina/Cordoba");
    }
}
