using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Holidays;
using NodaTime;

namespace Frappe.Geo.UnitTests;

public sealed class HolidayYearTests
{
    [Theory]
    [InlineData("AR", "America/Argentina/Buenos_Aires")]
    [InlineData("US", "America/New_York")]
    [InlineData("BV", "UTC")]
    public void A_country_takes_its_first_zone_tab_zone_else_utc(string country, string zone) =>
        HolidayYear.ZoneOf(CountryCode.Create(country).Value).Id.ShouldBe(zone);

    [Fact]
    public void A_country_without_a_zone_turns_its_year_at_utc_midnight() =>
        HolidayYear
            .Current(CountryCode.Create("BV").Value, Instant.FromUtc(2026, 12, 31, 23, 0))
            .ShouldBe((2026, 3600));

    [Fact]
    public void Early_in_the_year_the_cap_is_a_day() =>
        HolidayYear
            .Current(CountryCode.Create("AR").Value, Instant.FromUtc(2026, 6, 1, 12, 0))
            .ShouldBe((2026, 86_400));
}
