using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;

namespace Frappe.Geo.UnitTests.Cities;

public sealed class TimeZoneIdConverterTests
{
    [Theory]
    [InlineData("Europe/Kiev")]
    [InlineData("America/Argentina/Cordoba")]
    public void A_stored_zone_is_read_back_as_stored(string stored)
    {
        TimeZoneIdConverter converter = new();

        TimeZoneId zone = converter.ConvertFromProvider(stored).ShouldBeOfType<TimeZoneId>();

        zone.Value.ShouldBe(stored);
        converter.ConvertToProvider(zone).ShouldBe(stored);
    }
}
