using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;

namespace Frappe.Geo.UnitTests.Cities;

public sealed class CityIdsTests
{
    [Fact]
    public void The_same_indec_id_always_gives_the_same_valid_uuid_v7()
    {
        CityId first = CityIds.From("14014010");
        CityId second = CityIds.From("14014010");

        first.ShouldBe(second);
        first.Value.Version.ShouldBe(7);
        CityId.From(first.Value).IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData("14014010", "1401401003")]
    [InlineData("06630020", "06638040")]
    public void Different_indec_ids_give_different_ids(string one, string other)
    {
        CityIds.From(one).ShouldNotBe(CityIds.From(other));
    }

    [Fact]
    public void The_id_carries_the_fixed_timestamp_and_the_indec_value()
    {
        CityId id = CityIds.From("1401401003");

        // 2026-01-01T00:00:00Z in milliseconds, version 7, the value's 12 high bits (zero), the RFC variant, then
        // 1401401003 in hexadecimal.
        id.Value.ToString().ShouldBe("019b76da-a800-7000-8000-00005387aeab");
    }
}
